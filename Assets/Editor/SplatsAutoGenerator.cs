using UnityEditor;
using System.IO;
using GaussianSplatting.Editor;
using UnityEngine;

public class BatchSplatImporter
{
    [MenuItem("Tools/Gerar Assets (Versão Final Estável)")]
    public static void Run()
    {
        // 1. CONFIGURAÇÃO DE CAMINHOS
        // Certifique-se de que o caminho termine sem a barra final
        string plysPath = "C:/Users/felip/Documents/Mestrado/Insetos/SplatModels/Mari2";
        string outPath = "Assets/GaussianAssets";

        // Garante que a pasta de destino exista no projeto
        if (!Directory.Exists(outPath))
        {
            Directory.CreateDirectory(outPath);
            AssetDatabase.ImportAsset(outPath);
        }

        // 2. BUSCA DE ARQUIVOS
        string[] arquivos = Directory.GetFiles(plysPath, "*.ply");
        int totalArquivos = arquivos.Length;
        int novosProcessados = 0;

        Debug.Log($"--- Iniciando varredura de {totalArquivos} arquivos PLY ---");

        for (int i = 0; i < totalArquivos; i++)
        {
            string arquivoAtual = arquivos[i];
            string nomeSemExtensao = Path.GetFileNameWithoutExtension(arquivoAtual);

            // IMPORTANTE: Verifique se o plugin gera .asset ou .gaussianAsset e ajuste aqui
            string caminhoAssetEsperado = Path.Combine(outPath, nomeSemExtensao + ".asset");

            // 3. FILTRO DE EXISTÊNCIA (Pula o que já foi feito)
            if (File.Exists(caminhoAssetEsperado))
            {
                continue;
            }

            // 4. FEEDBACK VISUAL E CANCELAMENTO
            float progresso = (float)i / totalArquivos;
            bool cancelar = EditorUtility.DisplayCancelableProgressBar(
                "Importação Massiva 3DGS",
                $"Processando ({i + 1}/{totalArquivos}): {nomeSemExtensao}",
                progresso
            );

            if (cancelar)
            {
                Debug.LogWarning("Importação cancelada pelo usuário.");
                break;
            }

            try
            {
                // 5. CRIAÇÃO DO ASSET
                // Usamos VeryHigh conforme seu requisito para o YOLO
                GaussianSplatAssetCreator.CreateAssetSilent(
                    arquivoAtual,
                    outPath,
                    GaussianSplatAssetCreator.DataQuality.VeryHigh
                );

                // 6. LIMPEZA CRÍTICA DE MEMÓRIA (O segredo para não travar)

                // Limpa o cache de Undo que acumula dados de malhas e texturas
                Undo.ClearAll();

                // Descarrega os buffers de memória que o plugin deixa abertos
                EditorUtility.UnloadUnusedAssetsImmediate();

                // Força o Garbage Collector do C# a limpar a RAM imediatamente
                System.GC.Collect();

                novosProcessados++;

                // 7. SALVAMENTO PERIÓDICO NO DISCO
                // A cada 5 arquivos, força o Unity a escrever no SSD e liberar cache de escrita
                if (novosProcessados % 5 == 0)
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Estabilidade] {novosProcessados} novos assets salvos. RAM limpa.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Erro ao processar {nomeSemExtensao}: {e.Message}");
            }
        }

        // 8. FINALIZAÇÃO
        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh(); // Atualiza a Project Window do Unity
        Debug.Log($"--- Processo concluído! {novosProcessados} novos assets criados ---");
    }
}