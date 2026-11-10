# DR 3DGS — execução local (Windows + VS Code)

## Preparar o ambiente (uma vez)
No terminal do VS Code (PowerShell), dentro desta pasta:

```powershell
py -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pip install ipykernel   # só se quiser rodar célula a célula ("Run Cell")
```

No VS Code: `Ctrl+Shift+P` → "Python: Select Interpreter" → escolha `.venv`.

## Rodar pelo terminal
```powershell
python dr_3dgs_local.py --entrada "C:\caminho\mari2_hires_SuperLimpa_artigo_final.ply" --amostras 100 --seed 42
```

Opções:
- `--modo ambos|photometric|procedural|original` (padrão: `ambos`, que alterna A/B)
- `--saida "C:\pasta"` (padrão: `<pasta do .ply>\dataset_sintetico_experimentos`)
- `--sem-pruning`, `--alpha-thresh 0.10`, `--anisotropy-thresh 10`
- `--centralizar` move o centro da AABB do modelo para a origem (recomendado para a Unity)

## Rodar célula a célula
Abra `dr_3dgs_local.py`, edite o dicionário `CONFIG` e clique em "Run Cell" acima de cada `# %%`.

## Saída
- `<modelo>_PipeA_Photometric_0000.ply`, `<modelo>_PipeB_Procedural_0001.ply`, ...
- `manifest.csv`: os parâmetros sorteados para cada arquivo (λ, Δcolor, ângulos, γ_spec, oitavas, ...).
  A mesma `--seed` gera exatamente os mesmos arquivos.
