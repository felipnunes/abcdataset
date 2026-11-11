using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Lê as posições (x, y, z) e a opacidade dos splats direto do .ply de origem.
// Usado para calcular bounding boxes justas (projeção dos centros das gaussianas)
// em vez de projetar a AABB 3D do asset, que superestima a caixa.
public class SplatPointCloud
{
    public Vector3[] points;       // posições no espaço local do asset (após correção de sinais)
    public Vector3 axisSigns = Vector3.one;
    public string sourcePath;
    public int totalSplats;

    // Lê um .ply binário little-endian (formato padrão do 3DGS).
    // minOpacity: descarta splats quase transparentes (sigmoid(opacity) < minOpacity).
    // maxPoints: subamostra uniforme para limitar o custo da projeção por amostra.
    public static SplatPointCloud Load(string plyPath, float minOpacity, int maxPoints)
    {
        using (var fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        using (var br = new BinaryReader(fs))
        {
            // --- Cabeçalho ---
            var props = new List<KeyValuePair<string, string>>(); // (tipo, nome) da element vertex
            int vertexCount = -1;
            bool inVertex = false, binaryLE = false;
            var line = new StringBuilder();
            while (true)
            {
                line.Length = 0;
                int b;
                while ((b = fs.ReadByte()) != -1 && b != '\n') line.Append((char)b);
                if (b == -1) throw new Exception("Cabeçalho PLY incompleto: " + plyPath);
                string l = line.ToString().Trim();
                if (l == "end_header") break;
                string[] tok = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;
                if (tok[0] == "format") binaryLE = tok.Length > 1 && tok[1] == "binary_little_endian";
                else if (tok[0] == "element")
                {
                    inVertex = tok[1] == "vertex";
                    if (inVertex) vertexCount = int.Parse(tok[2], CultureInfo.InvariantCulture);
                    else if (vertexCount >= 0) { /* elementos depois de vertex não são lidos */ }
                }
                else if (tok[0] == "property" && inVertex)
                {
                    if (tok[1] == "list") throw new Exception("PLY com propriedade 'list' em vertex não suportado: " + plyPath);
                    props.Add(new KeyValuePair<string, string>(tok[1], tok[2]));
                }
            }
            if (!binaryLE) throw new Exception("Somente PLY binary_little_endian é suportado: " + plyPath);
            if (vertexCount <= 0) throw new Exception("PLY sem vértices: " + plyPath);

            int stride = 0, ox = -1, oy = -1, oz = -1, oOpacity = -1;
            string tx = null, ty = null, tz = null, tOp = null;
            foreach (var p in props)
            {
                int size = TypeSize(p.Key);
                if (p.Value == "x") { ox = stride; tx = p.Key; }
                else if (p.Value == "y") { oy = stride; ty = p.Key; }
                else if (p.Value == "z") { oz = stride; tz = p.Key; }
                else if (p.Value == "opacity") { oOpacity = stride; tOp = p.Key; }
                stride += size;
            }
            if (ox < 0 || oy < 0 || oz < 0) throw new Exception("PLY sem x/y/z: " + plyPath);

            // --- Dados ---
            byte[] row = new byte[stride];
            var pts = new List<Vector3>(Mathf.Min(vertexCount, maxPoints) + 16);
            float logitMin = minOpacity > 0f ? Mathf.Log(minOpacity / (1f - minOpacity)) : float.NegativeInfinity;

            // Lê todos os splats (filtrando por opacidade), depois subamostra.
            // Opacidade é testada antes da subamostragem para não perder estruturas finas.
            var all = new List<Vector3>(vertexCount);
            Vector3 rawMin = Vector3.positiveInfinity, rawMax = Vector3.negativeInfinity;
            for (int i = 0; i < vertexCount; i++)
            {
                int read = br.Read(row, 0, stride);
                if (read < stride) break;
                var v = new Vector3(ReadAsFloat(row, ox, tx), ReadAsFloat(row, oy, ty), ReadAsFloat(row, oz, tz));
                rawMin = Vector3.Min(rawMin, v);
                rawMax = Vector3.Max(rawMax, v);
                if (oOpacity >= 0 && ReadAsFloat(row, oOpacity, tOp) < logitMin) continue;
                all.Add(v);
            }
            if (all.Count == 0) throw new Exception("Nenhum splat acima da opacidade mínima em: " + plyPath);
            int keepStep = Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)Mathf.Max(1, maxPoints)));

            // Sempre mantém os pontos extremos de cada eixo (garante que pontas de antenas/pernas
            // continuem dentro da caixa mesmo com subamostragem).
            var extremes = new int[6];
            for (int i = 0; i < all.Count; i++)
            {
                Vector3 v = all[i];
                if (v.x < all[extremes[0]].x) extremes[0] = i;
                if (v.x > all[extremes[1]].x) extremes[1] = i;
                if (v.y < all[extremes[2]].y) extremes[2] = i;
                if (v.y > all[extremes[3]].y) extremes[3] = i;
                if (v.z < all[extremes[4]].z) extremes[4] = i;
                if (v.z > all[extremes[5]].z) extremes[5] = i;
            }
            for (int i = 0; i < all.Count; i += keepStep) pts.Add(all[i]);
            foreach (int e in extremes) pts.Add(all[e]);

            return new SplatPointCloud
            {
                points = pts.ToArray(), sourcePath = plyPath, totalSplats = vertexCount,
                rawMin = rawMin, rawMax = rawMax
            };
        }
    }

    public Vector3 rawMin, rawMax; // AABB de todos os splats do PLY (sem filtro de opacidade)

    // Os bounds do asset são calculados pelo importador sobre TODOS os splats.
    // Compara com a AABB do PLY testando as 8 combinações de sinal de eixo, para detectar
    // se o importador inverteu algum eixo. Retorna false se nenhuma bater.
    public bool MatchAssetBounds(Vector3 assetMin, Vector3 assetMax, out Vector3 signs)
    {
        signs = Vector3.one;
        Vector3 mn = rawMin, mx = rawMax;
        float tol = Mathf.Max(1e-5f, 0.01f * (assetMax - assetMin).magnitude);

        for (int s = 0; s < 8; s++)
        {
            Vector3 sg = new Vector3((s & 1) != 0 ? -1 : 1, (s & 2) != 0 ? -1 : 1, (s & 4) != 0 ? -1 : 1);
            Vector3 a = Vector3.Scale(mn, sg), b = Vector3.Scale(mx, sg);
            Vector3 smin = Vector3.Min(a, b), smax = Vector3.Max(a, b);
            if ((smin - assetMin).magnitude < tol && (smax - assetMax).magnitude < tol)
            {
                signs = sg;
                return true;
            }
        }
        return false;
    }

    public void ApplySigns(Vector3 signs)
    {
        axisSigns = signs;
        if (signs == Vector3.one) return;
        for (int i = 0; i < points.Length; i++) points[i] = Vector3.Scale(points[i], signs);
    }

    static int TypeSize(string t)
    {
        switch (t)
        {
            case "char": case "uchar": case "int8": case "uint8": return 1;
            case "short": case "ushort": case "int16": case "uint16": return 2;
            case "int": case "uint": case "float": case "int32": case "uint32": case "float32": return 4;
            case "double": case "float64": return 8;
            default: throw new Exception("Tipo PLY desconhecido: " + t);
        }
    }

    static float ReadAsFloat(byte[] row, int offset, string t)
    {
        switch (t)
        {
            case "float": case "float32": return BitConverter.ToSingle(row, offset);
            case "double": case "float64": return (float)BitConverter.ToDouble(row, offset);
            case "int": case "int32": return BitConverter.ToInt32(row, offset);
            case "uint": case "uint32": return BitConverter.ToUInt32(row, offset);
            case "short": case "int16": return BitConverter.ToInt16(row, offset);
            case "ushort": case "uint16": return BitConverter.ToUInt16(row, offset);
            case "char": case "int8": return (sbyte)row[offset];
            default: return row[offset];
        }
    }
}
