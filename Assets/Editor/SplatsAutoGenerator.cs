using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GaussianSplatting.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

// Importação em lote de .ply de 3DGS para GaussianSplatAsset.
//
// Por que a RAM estourava antes:
//  1. O pacote vazava o .ply inteiro em memória nativa a cada import (NativeArray Persistent nunca
//     liberado em GaussianFileReader.ReadFile). Corrigido no pacote.
//  2. Todos os arquivos eram processados num único frame do Editor: a memória temporária por frame
//     (Allocator.Temp, objetos aguardando descarga) só é liberada entre frames.
//  3. Cada modelo chamava AssetDatabase.Refresh() global e selecionava o asset criado.
//
// Agora: um modelo por frame do Editor (EditorApplication.update), importação só dos arquivos do
// modelo, e a cada N modelos SaveAssets + UnloadUnusedAssetsImmediate + GC. A memória em uso aparece
// na barra de progresso e no Console.
public static class BatchSplatImporter
{
    // 1. CONFIGURAÇÃO DE CAMINHOS (sem barra no final)
    const string kPlysPath = "C:/Users/felip/Documents/Mestrado/Insetos/ArtigoSibgrapi/.ply usados para gerar os datasets do artigo/Usados (alta variacao)/fly100_RD/fly100_RD";
    const string kOutPath = "Assets/GaussianAssets";
    const GaussianSplatAssetCreator.DataQuality kQuality = GaussianSplatAssetCreator.DataQuality.VeryHigh;

    // A cada quantos modelos salvar e descarregar o que não está em uso
    const int kCleanupEvery = 5;

    static Queue<string> s_Queue;
    static int s_Total, s_Done, s_Created, s_Failed, s_SinceCleanup;
    static long s_StartMemory;
    static double s_StartTime;
    static bool s_Running;

    [MenuItem("Tools/Gerar Assets (Versão Final Estável)")]
    public static void Run()
    {
        if (s_Running)
        {
            Debug.LogWarning("A importação em lote já está rodando.");
            return;
        }
        if (!Directory.Exists(kPlysPath))
        {
            Debug.LogError("Pasta de .ply não encontrada: " + kPlysPath);
            return;
        }
        if (!Directory.Exists(kOutPath))
        {
            Directory.CreateDirectory(kOutPath);
            AssetDatabase.ImportAsset(kOutPath);
        }

        // 2. BUSCA DE ARQUIVOS (pula os que já têm .asset)
        var todos = Directory.GetFiles(kPlysPath, "*.ply").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var pendentes = todos.Where(f => !File.Exists(Path.Combine(kOutPath, Path.GetFileNameWithoutExtension(f) + ".asset"))).ToList();

        s_Queue = new Queue<string>(pendentes);
        s_Total = pendentes.Count;
        s_Done = s_Created = s_Failed = s_SinceCleanup = 0;
        s_StartMemory = Profiler.GetTotalAllocatedMemoryLong();
        s_StartTime = EditorApplication.timeSinceStartup;

        Debug.Log($"--- {todos.Count} arquivos PLY encontrados, {s_Total} a importar ({todos.Count - s_Total} já existiam) ---");
        if (s_Total == 0) return;

        s_Running = true;
        EditorApplication.update += Step;
    }

    // Processa UM modelo por chamada (um frame do Editor).
    static void Step()
    {
        if (!s_Running) return;

        if (s_Queue.Count == 0)
        {
            Finish("Processo concluído");
            return;
        }

        string arquivo = s_Queue.Peek();
        string nome = Path.GetFileNameWithoutExtension(arquivo);
        long memMB = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

        bool cancelar = EditorUtility.DisplayCancelableProgressBar(
            "Importação em lote 3DGS",
            $"({s_Done + 1}/{s_Total}) {nome}   |   memória em uso: {memMB} MB",
            s_Total > 0 ? (float)s_Done / s_Total : 1f);
        if (cancelar)
        {
            Finish("Importação cancelada pelo usuário");
            return;
        }

        s_Queue.Dequeue();
        try
        {
            GaussianSplatAssetCreator.CreateAssetSilent(arquivo, kOutPath, kQuality);
            s_Created++;
        }
        catch (Exception e)
        {
            s_Failed++;
            Debug.LogError($"Erro ao processar {nome}: {e.Message}");
        }
        s_Done++;
        s_SinceCleanup++;

        // 3. LIMPEZA PERIÓDICA (entre frames, quando nada do import anterior está mais na pilha)
        if (s_SinceCleanup >= kCleanupEvery)
        {
            s_SinceCleanup = 0;
            Cleanup();
            Debug.Log($"[Lote] {s_Done}/{s_Total} | memória em uso: {Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB " +
                      $"| reservada: {Profiler.GetTotalReservedMemoryLong() / (1024 * 1024)} MB");
        }
    }

    static void Cleanup()
    {
        AssetDatabase.SaveAssets();
        Undo.ClearAll();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    static void Finish(string status)
    {
        EditorApplication.update -= Step;
        s_Running = false;
        EditorUtility.ClearProgressBar();
        Cleanup();
        AssetDatabase.Refresh();

        double secs = EditorApplication.timeSinceStartup - s_StartTime;
        long memDelta = (Profiler.GetTotalAllocatedMemoryLong() - s_StartMemory) / (1024 * 1024);
        Debug.Log($"--- {status}: {s_Created} criados, {s_Failed} com erro, {s_Queue?.Count ?? 0} restantes, " +
                  $"{secs:F0} s. Variação de memória desde o início: {memDelta:+#;-#;0} MB ---");
        s_Queue = null;
    }
}
