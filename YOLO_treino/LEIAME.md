# Treino YOLO com os datasets da Unity

## Preparar o ambiente (uma vez)
No terminal do VS Code (PowerShell), dentro desta pasta:

```powershell
py -m venv .venv
.venv\Scripts\Activate.ps1
# Com GPU NVIDIA: instale primeiro o PyTorch com CUDA (comando em https://pytorch.org/get-started/locally/), ex.:
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
pip install -r requirements.txt
```

Sem o passo do PyTorch com CUDA, o treino roda na CPU (bem mais lento).

## Usar
1. Abra `train_yolo.ipynb` no VS Code.
2. No canto superior direito, escolha o kernel `.venv`.
3. Ajuste a célula **1. Configuração** (execuções, modelo, épocas, fotos reais) e rode as células em ordem.

A célula 3 confere imagens e labels e mostra algumas imagens com as caixas desenhadas antes de treinar.

## Resultado
Fica em `runs/<nome>/`:
- `weights/best.pt`: melhor modelo
- `results.csv` e `results.png`: métricas por época
- `labels.jpg` e `train_batch0.jpg`: caixas como o YOLO leu; bom para conferir as bounding boxes
- `data_combinado.yaml` e `runs_usadas.txt`: quais execuções da Unity entraram no treino
