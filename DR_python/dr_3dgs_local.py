# %% [markdown]
# # Domain Randomization para 3DGS — versão local (VS Code)
# Adaptado do notebook `3dgs_domain_randomization_v4_sibgrapi_wip.ipynb` (Colab).
#
# Pode ser usado de duas formas:
#   1. Terminal:  python dr_3dgs_local.py --entrada "C:/caminho/modelo.ply" --amostras 100
#   2. VS Code:   abra este arquivo e use "Run Cell" nas células "# %%" (Jupyter interativo).
#                 Nesse modo, edite o bloco CONFIG abaixo.
#
# Mudanças em relação ao notebook (a matemática dos pipelines A e B foi mantida):
#   - Removidos Google Drive e o os.kill do Colab; caminhos via pathlib (funciona no Windows).
#   - Removida a sobrescrita "final_data = canonical_data / Original" que fazia o loop
#     exportar SEMPRE o modelo original. Agora isso é uma opção (--modo original).
#   - Gerador aleatório único e semeável (np.random.default_rng). No notebook,
#     spatial_noise() chamava np.random.seed(), o que reiniciava o RNG global e tornava
#     as amostras seguintes correlacionadas/repetidas.
#   - Gera um manifest.csv com os parâmetros de cada .ply (rastreabilidade para a Unity
#     e para a ablação do artigo).
#   - Opção --centralizar: translada o modelo para que o centro da AABB fique na origem
#     (ajuda a câmera da Unity, que mira no pivô do objeto). Desligada por padrão.

# %%
import argparse
import csv
import sys
from pathlib import Path

import numpy as np
from plyfile import PlyData, PlyElement
from scipy.spatial.transform import Rotation as R
from scipy.special import expit

# ------------------------------------------------------------------------------
# CONFIG (usado quando o arquivo é executado célula a célula no VS Code, sem argumentos)
# ------------------------------------------------------------------------------
CONFIG = {
    "entrada": r"C:\Users\felip\Documents\Mestrado\Insetos\ArtigoSibgrapi\mari2_hires_SuperLimpa_artigo_final.ply",
    "saida": None,            # None -> <pasta do .ply>/dataset_sintetico_experimentos
    "amostras": 100,
    "modo": "ambos",          # "ambos" (alterna A/B), "photometric", "procedural", "original"
    "seed": 42,
    "sem_pruning": False,
    "alpha_thresh": 0.10,
    "anisotropy_thresh": 10.0,
    "centralizar": False,
}


# ==============================================================================
# MÓDULO 1: SPATIO-OPTICAL PRUNING
# ==============================================================================
def apply_spatio_optical_pruning(vertex_data, alpha_thresh=0.10, anisotropy_thresh=10.0):
    print("--- EXECUTANDO SPATIO-OPTICAL PRUNING ---")
    opacidades = expit(vertex_data["opacity"])
    scales = np.vstack([
        np.exp(vertex_data["scale_0"]),
        np.exp(vertex_data["scale_1"]),
        np.exp(vertex_data["scale_2"]),
    ]).T
    s_max = np.max(scales, axis=1)
    s_min = np.maximum(np.min(scales, axis=1), 1e-6)  # prevenção div/0
    anisotropia = s_max / s_min
    mascara_floaters = (opacidades < alpha_thresh) & (anisotropia > anisotropy_thresh)

    canonical_data = vertex_data[~mascara_floaters]
    print(f"Floaters removidos (anomalias): {int(np.sum(mascara_floaters))}")
    print(f"Splats canônicos válidos: {len(canonical_data)}\n")
    return canonical_data


def centralizar_modelo(data):
    """Translada as posições para que o centro da AABB fique em (0,0,0)."""
    out = data.copy()
    centro = np.array([
        (data["x"].min() + data["x"].max()) / 2,
        (data["y"].min() + data["y"].max()) / 2,
        (data["z"].min() + data["z"].max()) / 2,
    ])
    out["x"] = data["x"] - centro[0]
    out["y"] = data["y"] - centro[1]
    out["z"] = data["z"] - centro[2]
    print(f"Modelo centralizado (deslocamento aplicado: {-centro})")
    return out


def verificar_campos_sh(data):
    """Confere se o .ply tem SH de grau 3 (45 coeficientes f_rest)."""
    nomes = data.dtype.names
    faltando = [f"f_rest_{i}" for i in range(45) if f"f_rest_{i}" not in nomes]
    if faltando:
        raise ValueError(
            f"O .ply não tem SH de grau 3 completo (faltam {len(faltando)} campos f_rest_*). "
            "Os pipelines assumem 45 coeficientes AC."
        )


# ==============================================================================
# MÓDULO 2: PIPELINE A - EXTREME PHOTOMETRIC DR
# ==============================================================================
def get_aggressive_photometric_shift(rng):
    """Perturbações DC severas simulando falhas de WB e luzes artificiais."""
    base_shift = rng.uniform(-1.2, 1.2, 3)
    extreme = False
    if rng.random() > 0.8:  # 20% de chance de contaminação extrema (UV, lasers, flash)
        extreme_cast = np.zeros(3)
        extreme_cast[rng.integers(0, 3)] = rng.uniform(1.5, 3.0)
        base_shift += extreme_cast
        extreme = True
    return base_shift, extreme


def generate_photometric_dr(canonical_data, lambda_scale=1.0, delta_temp=(0, 0, 0),
                            rot_angles=(0, 0, 0), specular_scale=1.0):
    new_data = canonical_data.copy()

    # 1. DC Thermal Perturbation
    for c in range(3):
        new_data[f"f_dc_{c}"] = canonical_data[f"f_dc_{c}"] * lambda_scale + delta_temp[c]

    # 2. AC Wigner Rotation (apenas l=1)
    rot_matrix = R.from_euler("xyz", rot_angles, degrees=True).as_matrix()
    for color_idx in range(3):
        offset = color_idx * 15
        idx_y, idx_z, idx_x = f"f_rest_{offset}", f"f_rest_{offset + 1}", f"f_rest_{offset + 2}"
        sh1 = np.vstack((canonical_data[idx_x], canonical_data[idx_y], canonical_data[idx_z])).T
        rotated = sh1 @ rot_matrix.T
        new_data[idx_x] = rotated[:, 0] * specular_scale
        new_data[idx_y] = rotated[:, 1] * specular_scale
        new_data[idx_z] = rotated[:, 2] * specular_scale

        # 3. Escala para alta ordem (l > 1)
        for m in range(3, 15):
            new_data[f"f_rest_{offset + m}"] = canonical_data[f"f_rest_{offset + m}"] * specular_scale

    return new_data


# ==============================================================================
# MÓDULO 3: PIPELINE B - PROCEDURAL & VOLUMETRIC DR
# ==============================================================================
def generate_procedural_dr(canonical_data, rng, c_amp=3.0, c_shift=(-1.5, -1.5, -1.5)):
    new_data = canonical_data.copy()
    mu_x, mu_y, mu_z = canonical_data["x"], canonical_data["y"], canonical_data["z"]

    # Arquitetura do ruído sorteada por modelo
    num_octaves = int(rng.integers(1, 6))      # 1..5
    persistence = rng.uniform(0.15, 0.75)
    lacunarity = rng.uniform(1.5, 3.5)

    def spatial_noise(x, y, z):
        noise = np.zeros_like(x, dtype=np.float64)
        stretch = rng.uniform(0.2, 4.0, 3)
        amplitude = 1.0
        freq = rng.uniform(1.0, 20.0)
        for _ in range(num_octaves):
            ph = rng.uniform(0, 2 * np.pi, 3)
            nx = np.sin((x * stretch[0]) * freq + ph[0])
            ny = np.cos((y * stretch[1]) * freq + ph[1])
            nz = np.sin((z * stretch[2]) * freq + ph[2])
            noise += (nx * ny * nz) * amplitude
            amplitude *= persistence
            freq *= lacunarity
        return noise

    # Um campo de ruído independente por canal (o próprio rng garante o desalinhamento)
    noises = [spatial_noise(mu_x, mu_y, mu_z) for _ in range(3)]

    max_possible = sum(persistence ** i for i in range(num_octaves))
    noises = [np.clip(n / (max_possible + 1e-5), -1.0, 1.0) for n in noises]

    # Heaviside (bordas duras) vs contínuo (degradê)
    binarizado = bool(rng.random() > 0.5)
    if binarizado:
        noises = [(n > rng.uniform(-0.4, 0.4)).astype(np.float64) for n in noises]

    choices = rng.choice([-1, 1], size=3)
    dynamic_amp = c_amp * rng.uniform(0.3, 1.5)

    for c in range(3):
        new_data[f"f_dc_{c}"] = noises[c] * (choices[c] * dynamic_amp) + c_shift[c]

    # Zera componentes AC (visual matte)
    for i in range(45):
        new_data[f"f_rest_{i}"] = 0.0

    params = {
        "num_octaves": num_octaves, "persistence": persistence, "lacunarity": lacunarity,
        "binarizado": binarizado, "dynamic_amp": dynamic_amp,
    }
    return new_data, params


# ==============================================================================
# EXECUÇÃO
# ==============================================================================
def salvar_ply(data, caminho, comentarios=None):
    el = PlyElement.describe(data, "vertex")
    PlyData([el], text=False, comments=comentarios or []).write(str(caminho))


def executar(entrada, saida=None, amostras=100, modo="ambos", seed=42, sem_pruning=False,
             alpha_thresh=0.10, anisotropy_thresh=10.0, centralizar=False):
    entrada = Path(entrada)
    if not entrada.exists():
        sys.exit(f"[ERRO] Arquivo não encontrado: {entrada}")

    pasta_saida = Path(saida) if saida else entrada.parent / "dataset_sintetico_experimentos"
    pasta_saida.mkdir(parents=True, exist_ok=True)

    print(f"[OK] Modelo canônico: {entrada}")
    print(f"[OK] Saída: {pasta_saida}\n")

    rng = np.random.default_rng(seed)

    raw = PlyData.read(str(entrada))["vertex"].data
    verificar_campos_sh(raw)
    canonical = raw if sem_pruning else apply_spatio_optical_pruning(raw, alpha_thresh, anisotropy_thresh)
    if centralizar:
        canonical = centralizar_modelo(canonical)

    base = entrada.stem
    manifest_path = pasta_saida / "manifest.csv"
    campos = ["arquivo", "modelo_base", "pipeline", "indice", "seed_global",
              "lambda", "delta_r", "delta_g", "delta_b", "cast_extremo",
              "rot_x", "rot_y", "rot_z", "gamma_spec",
              "c_amp", "c_shift_r", "c_shift_g", "c_shift_b",
              "num_octaves", "persistence", "lacunarity", "binarizado", "dynamic_amp",
              "n_splats"]

    print(f"--- INICIANDO GERAÇÃO: {amostras} ARQUIVOS (modo={modo}, seed={seed}) ---")
    with open(manifest_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=campos)
        writer.writeheader()

        for i in range(amostras):
            linha = {k: "" for k in campos}
            if modo == "ambos":
                tipo = "photometric" if i % 2 == 0 else "procedural"
            else:
                tipo = modo

            if tipo == "photometric":
                lam = rng.uniform(0.2, 3.0)
                tint, extremo = get_aggressive_photometric_shift(rng)
                rot = rng.uniform(0, 360, 3)
                spec = rng.lognormal(mean=0.8, sigma=0.5)
                final = generate_photometric_dr(canonical, lam, tint, rot, spec)
                nome = "PipeA_Photometric"
                linha.update({"lambda": lam, "delta_r": tint[0], "delta_g": tint[1], "delta_b": tint[2],
                              "cast_extremo": extremo, "rot_x": rot[0], "rot_y": rot[1], "rot_z": rot[2],
                              "gamma_spec": spec})
            elif tipo == "procedural":
                c_amp = rng.uniform(2.0, 5.0)
                c_shift = rng.uniform(-2.0, 2.0, 3)
                final, p = generate_procedural_dr(canonical, rng, c_amp, c_shift)
                nome = "PipeB_Procedural"
                linha.update({"c_amp": c_amp, "c_shift_r": c_shift[0], "c_shift_g": c_shift[1],
                              "c_shift_b": c_shift[2], **p})
            elif tipo == "original":
                final = canonical
                nome = "Original"
            else:
                sys.exit(f"[ERRO] modo inválido: {modo}")

            # Nome inclui o modelo base -> a Unity consegue saber de qual espécime veio cada imagem
            arquivo = f"{base}_{nome}_{i:04d}.ply"
            salvar_ply(final, pasta_saida / arquivo,
                       comentarios=[f"dr_pipeline {nome}", f"dr_seed {seed}", f"dr_index {i}"])

            linha.update({"arquivo": arquivo, "modelo_base": base, "pipeline": nome, "indice": i,
                          "seed_global": seed, "n_splats": len(final)})
            writer.writerow(linha)

            if (i + 1) % 50 == 0 or i == 0 or i + 1 == amostras:
                print(f"Progresso: {i + 1}/{amostras} modelos salvos...")

    print(f"\n[SUCESSO] Lote concluído em: {pasta_saida}")
    print(f"[SUCESSO] Parâmetros de cada amostra em: {manifest_path}")


def main():
    p = argparse.ArgumentParser(description="Domain Randomization para modelos 3DGS (.ply)")
    p.add_argument("--entrada", required=True, help="Caminho do .ply canônico")
    p.add_argument("--saida", default=None, help="Pasta de saída (padrão: <pasta do .ply>/dataset_sintetico_experimentos)")
    p.add_argument("--amostras", type=int, default=100)
    p.add_argument("--modo", choices=["ambos", "photometric", "procedural", "original"], default="ambos")
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--sem-pruning", action="store_true", help="Pula o spatio-optical pruning")
    p.add_argument("--alpha-thresh", type=float, default=0.10)
    p.add_argument("--anisotropy-thresh", type=float, default=10.0)
    p.add_argument("--centralizar", action="store_true", help="Centraliza a AABB do modelo na origem")
    a = p.parse_args()
    executar(a.entrada, a.saida, a.amostras, a.modo, a.seed, a.sem_pruning,
             a.alpha_thresh, a.anisotropy_thresh, a.centralizar)


# %%
if __name__ == "__main__":
    # Sem argumentos e dentro do Jupyter interativo do VS Code -> usa CONFIG
    if len(sys.argv) == 1 or "ipykernel" in sys.modules:
        executar(**CONFIG)
    else:
        main()
