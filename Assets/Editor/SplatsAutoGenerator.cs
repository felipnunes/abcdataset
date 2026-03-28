using UnityEditor;
using System.IO;
using GaussianSplatting.Editor;

public class BatchSplatImporter
{
    [MenuItem("Tools/Gerar Todos os Assets")]
    public static void Run()
    {
        string plysPath = "C:/Users/felip/Documents/Mestrado/Insetos/SplatModels"; // Onde estão seus PLYs do Colab
        string outPath = "Assets/GaussianAssets";

        string[] arquivos = Directory.GetFiles(plysPath, "*.ply");

        foreach (string file in arquivos)
        {
            // Chama a função nativa que preparamos
            GaussianSplatAssetCreator.CreateAssetSilent(
                file,
                outPath,
                GaussianSplatAssetCreator.DataQuality.VeryHigh // Qualidade máxima para o YOLO
            );
        }

        AssetDatabase.Refresh(); // Atualiza o Unity para mostrar os novos arquivos
    }
}