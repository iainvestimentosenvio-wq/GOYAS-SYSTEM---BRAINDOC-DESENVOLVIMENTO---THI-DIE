#!/usr/bin/env python3
"""Core orchestrator helpers for Windows E2E round summaries and status output."""

from __future__ import annotations

import argparse
import csv
import glob
import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import Dict, List, Tuple


def now_utc() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_csv_rows(path: Path) -> List[Dict[str, str]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        return [dict(row) for row in reader]


def last_status(rows: List[Dict[str, str]], step: str) -> str:
    status = ""
    for row in rows:
        if row.get("step") == step:
            status = row.get("status", "")
    return status


def count_statuses(rows: List[Dict[str, str]]) -> Dict[str, int]:
    counts = {
        "PASS": 0,
        "FAIL": 0,
        "PARCIAL": 0,
        "BLOQUEADO": 0,
        "TOTAL": 0,
    }
    for row in rows:
        status = (row.get("status") or "").strip()
        if not status:
            continue
        counts["TOTAL"] += 1
        if status in counts:
            counts[status] += 1
    return counts


def evaluate_gate_g3(run_dir: Path) -> bool:
    vms = ["win10-lite", "win11-lite"]
    required = [
        "MSI-UNINST-01-PROGRAMFILES",
        "MSI-UNINST-03-REGISTRY",
        "INNO-02-DESKTOP",
        "INNO-03-STARTMENU",
        "INNO-04-REGISTRY",
        "INNO-UNINST-01-PROGRAMFILES",
    ]

    for vm in vms:
        msi_matches = sorted(glob.glob(str(run_dir / f"{vm}-guest-msi-results-*.json")))
        inno_matches = sorted(glob.glob(str(run_dir / f"{vm}-guest-inno-results-*.json")))
        if not msi_matches or not inno_matches:
            return False

        with open(msi_matches[-1], "r", encoding="utf-8-sig") as f:
            msi = json.load(f)
        with open(inno_matches[-1], "r", encoding="utf-8-sig") as f:
            inno = json.load(f)

        statuses: Dict[str, str] = {}
        for row in msi.get("results", []):
            statuses[row.get("id", "")] = row.get("status", "")
        for row in inno.get("results", []):
            statuses[row.get("id", "")] = row.get("status", "")

        for rid in required:
            if statuses.get(rid) != "PASS":
                return False

    return True


def evaluate_gate_g4(run_dir: Path) -> bool:
    vms = ["win10-lite", "win11-lite"]
    limits = {
        "msi_install_p95_seconds": 90.0,
        "msi_uninstall_p95_seconds": 45.0,
        "inno_install_p95_seconds": 90.0,
        "inno_uninstall_p95_seconds": 45.0,
    }

    for vm in vms:
        matches = sorted(glob.glob(str(run_dir / f"{vm}-guest-regressao-windows-*.json")))
        if not matches:
            return False

        with open(matches[-1], "r", encoding="utf-8-sig") as f:
            data = json.load(f)
        perf = data.get("performance", {})

        for key, limit in limits.items():
            value = perf.get(key)
            if value is None:
                return False
            try:
                metric = float(value)
            except Exception:
                return False
            if metric > limit:
                return False

    return True


def parse_transport_modes(run_dir: Path) -> Dict[str, str]:
    result: Dict[str, str] = {}
    for log_file in sorted(run_dir.glob("run_regressao_*_bundle_transfer_metrics.log")):
        vm = log_file.name.replace("run_regressao_", "").replace("_bundle_transfer_metrics.log", "")
        mode = "unknown"
        try:
            for line in log_file.read_text(encoding="utf-8", errors="replace").splitlines():
                if line.startswith("transport_mode="):
                    mode = line.split("=", 1)[1].strip()
        except Exception:
            mode = "unknown"
        result[vm] = mode
    return result


def mandatory_contract_pass(mandatory_csv: Path) -> bool:
    rows = load_csv_rows(mandatory_csv)
    for row in rows:
        if row.get("step") == "suite_test_installer_contract_static":
            return row.get("status") == "PASS"
    return False


def write_file(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def build_readiness(g1: str, g2: str, g3: str, g4: str, contract_ok: bool) -> Tuple[bool, List[str]]:
    blockers: List[str] = []
    if g1 != "PASS":
        blockers.append("G1 (QGA nas duas VMs) nao passou")
    if g2 != "PASS":
        blockers.append("G2 (regressao completa nas duas VMs) nao passou")
    if g3 != "PASS":
        blockers.append("G3 (paridade MSI/Inno em contrato) nao passou")
    if g4 != "PASS":
        blockers.append("G4 (performance p95) nao passou")
    if not contract_ok:
        blockers.append("suite_test_installer_contract_static nao passou")
    return (len(blockers) == 0, blockers)


def check_artifact_freshness(installador_root: Path) -> Tuple[bool, str]:
    script = installador_root / "testes" / "windows" / "test-artifact-freshness.sh"
    if not script.exists():
        return False, f"script ausente: {script}"
    proc = subprocess.run(
        ["bash", str(script)],
        cwd=str(installador_root),
        capture_output=True,
        text=True,
        env={**os.environ, "PROTONS_ALLOW_STALE_ARTIFACTS": "0"},
    )
    output = (proc.stdout or "") + (proc.stderr or "")
    return (proc.returncode == 0, output.strip())


def git_code_revision(repo_root: Path) -> Dict[str, object]:
    commit = "unknown"
    short_revision = "unknown"
    dirty = True

    try:
        proc_commit = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            check=False,
        )
        if proc_commit.returncode == 0:
            commit = (proc_commit.stdout or "").strip() or "unknown"
    except Exception:
        commit = "unknown"

    try:
        proc_short = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            check=False,
        )
        if proc_short.returncode == 0:
            short_revision = (proc_short.stdout or "").strip() or "unknown"
    except Exception:
        short_revision = "unknown"

    try:
        proc_dirty = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            check=False,
        )
        if proc_dirty.returncode == 0:
            dirty = bool((proc_dirty.stdout or "").strip())
    except Exception:
        dirty = True

    return {
        "commit": commit,
        "short_revision": short_revision,
        "dirty": dirty,
    }


def compute_status_stale(
    installador_root: Path,
    run_dir: Path,
    csv_file: Path,
    mandatory_csv: Path,
) -> Tuple[bool, List[str], float]:
    critical_sources = [
        installador_root / "comum" / "version.env",
        installador_root / "comum" / "scripts" / "windows-autonomous-round.sh",
        installador_root / "comum" / "scripts" / "windows-e2e-sequencial.sh",
        installador_root / "comum" / "scripts" / "windows_e2e_orchestrator.py",
        installador_root / "testes" / "windows" / "installer-contract.json",
        installador_root / "windows" / "innosetup" / "protons-setup.iss",
        installador_root / "windows" / "scripts" / "build-msi.ps1",
        installador_root / "windows" / "scripts" / "build-inno.ps1",
        installador_root / "windows" / "scripts" / "prepare-inno-ux-assets.ps1",
        installador_root / "windows" / "wix" / "Product.wxs",
        installador_root / "windows" / "wix" / "Components.wxs",
    ]

    evidence_files = [csv_file, mandatory_csv]
    if run_dir.exists():
        evidence_files.extend([p for p in run_dir.rglob("*") if p.is_file()])

    evidence_mtimes = []
    for evidence in evidence_files:
        try:
            evidence_mtimes.append(evidence.stat().st_mtime)
        except Exception:
            continue

    if not evidence_mtimes:
        return True, ["evidence_missing"], 0.0

    reference_mtime = max(evidence_mtimes)
    stale_sources: List[str] = []
    for source in critical_sources:
        try:
            if source.exists() and source.stat().st_mtime > reference_mtime:
                stale_sources.append(str(source))
        except Exception:
            continue

    return (len(stale_sources) > 0, stale_sources, reference_mtime)


def main() -> int:
    parser = argparse.ArgumentParser(description="Windows E2E orchestrator core")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-dir", required=True)
    parser.add_argument("--csv-file", required=True)
    parser.add_argument("--summary-md", required=True)
    parser.add_argument("--gates-md", required=True)
    parser.add_argument("--mandatory-csv", required=True)
    parser.add_argument("--vm-order", default="win10-lite,win11-lite")
    parser.add_argument("--cooldown-sec", default="15")
    parser.add_argument("--bootstrap-mode", default="manual-ready")
    parser.add_argument("--manual-fallback-on-auto-fail", default="1")
    parser.add_argument("--strict-signature", default="0")
    parser.add_argument("--signature-profile", default="technical", choices=["technical", "production"])
    args = parser.parse_args()

    run_dir = Path(args.run_dir)
    csv_file = Path(args.csv_file)
    summary_md = Path(args.summary_md)
    gates_md = Path(args.gates_md)
    mandatory_csv = Path(args.mandatory_csv)

    rows = load_csv_rows(csv_file)
    counts = count_statuses(rows)

    g1_win10 = last_status(rows, "bootstrap_qga_win10-lite_result") or "SEM_RESULTADO"
    g1_win11 = last_status(rows, "bootstrap_qga_win11-lite_result") or "SEM_RESULTADO"
    g2_win10 = last_status(rows, "run_regressao_win10-lite_result") or "SEM_RESULTADO"
    g2_win11 = last_status(rows, "run_regressao_win11-lite_result") or "SEM_RESULTADO"

    g1 = "PASS" if g1_win10 == "PASS" and g1_win11 == "PASS" else "NAO"
    g2 = "PASS" if g2_win10 == "PASS" and g2_win11 == "PASS" else "NAO"
    g3 = "PASS" if evaluate_gate_g3(run_dir) else "NAO"
    g4 = "PASS" if evaluate_gate_g4(run_dir) else "NAO"

    summary_lines = [
        "# Rodada Windows Sequencial",
        "",
        f"- RunId: `{args.run_id}`",
        f"- TimestampUTC: `{now_utc()}`",
        f"- Ordem VMs: `{args.vm_order}`",
        f"- Cooldown: `{args.cooldown_sec}s`",
        f"- Bootstrap mode: `{args.bootstrap_mode}`",
        f"- Manual fallback on auto fail: `{args.manual_fallback_on_auto_fail}`",
        f"- Strict signature: `{args.strict_signature}`",
        f"- Signature profile: `{args.signature_profile}`",
        f"- CSV: `{csv_file}`",
        f"- Mandatory suite: `{mandatory_csv}`",
        "",
        "## Totais da rodada",
        f"- PASS: {counts['PASS']}",
        f"- FAIL: {counts['FAIL']}",
        f"- PARCIAL: {counts['PARCIAL']}",
        f"- BLOQUEADO: {counts['BLOQUEADO']}",
        f"- TOTAL: {counts['TOTAL']}",
        "",
        "## Gates",
        f"- G1 (QGA ativo nas duas VMs): {g1}",
        f"- G2 (regressao completa nas duas VMs): {g2}",
        f"- G3 (6 FAIL historicos resolvidos): {g3}",
        f"- G4 (p95 MSI/Inno dentro da meta): {g4}",
        "",
        "## Evidencias da rodada",
    ]

    for row in rows:
        step = row.get("step", "")
        status = row.get("status", "")
        evidence = row.get("evidence_file", "")
        summary_lines.append(f"- `{step}` | `{status}` | `{evidence}`")

    write_file(summary_md, "\n".join(summary_lines) + "\n")

    gates_lines = [
        "# Gates Summary",
        "",
        f"- RunId: `{args.run_id}`",
        f"- G1: {g1} (win10={g1_win10}, win11={g1_win11})",
        f"- G2: {g2} (win10={g2_win10}, win11={g2_win11})",
        f"- G3: {g3}",
        f"- G4: {g4}",
        "",
        "## Regras de score",
        "- Baselines: P1=30, P2=58, P5=45, P7=78, P9=42",
    ]

    gates_lines.append("- Aplicar G1: P7 78->80, P9 42->45" if g1 == "PASS" else "- G1 nao atingido: sem delta em P7/P9 por G1")
    gates_lines.append("- Aplicar G2: P1 30->45, P9 45->55" if g2 == "PASS" else "- G2 nao atingido: sem delta em P1/P9 por G2")
    gates_lines.append("- Aplicar G3: P2 58->75, P5 45->60" if g3 == "PASS" else "- G3 nao atingido: sem delta em P2/P5")
    gates_lines.append("- Aplicar G4: P1 45->60" if g4 == "PASS" else "- G4 nao atingido: sem delta adicional em P1")

    write_file(gates_md, "\n".join(gates_lines) + "\n")

    contract_ok = mandatory_contract_pass(mandatory_csv)

    installador_root = Path(__file__).resolve().parents[2]
    artifact_fresh_ok, freshness_output = check_artifact_freshness(installador_root)
    code_revision = git_code_revision(installador_root)
    status_stale, stale_sources, status_reference_mtime = compute_status_stale(
        installador_root=installador_root,
        run_dir=run_dir,
        csv_file=csv_file,
        mandatory_csv=mandatory_csv,
    )
    if status_reference_mtime > 0:
        from datetime import datetime, timezone

        status_reference_utc = datetime.fromtimestamp(
            status_reference_mtime, tz=timezone.utc
        ).strftime("%Y-%m-%dT%H:%M:%SZ")
    else:
        status_reference_utc = ""

    ready_for_real_test, blockers = build_readiness(g1, g2, g3, g4, contract_ok)
    if not artifact_fresh_ok:
        ready_for_real_test = False
        blockers.append("test-artifact-freshness.sh falhou")
    if status_stale:
        ready_for_real_test = False
        blockers.append("status_stale=true (fontes criticas mudaram apos a rodada)")

    transport_modes = parse_transport_modes(run_dir)

    status_dir = installador_root / "saida" / "status"
    status_dir.mkdir(parents=True, exist_ok=True)

    latest_json = status_dir / "latest-status.json"
    latest_md = status_dir / "latest-status.md"
    latest_csv = status_dir / "latest-status.csv"

    payload = {
        "schema_version": 1,
        "generated_at_utc": now_utc(),
        "source_of_truth": "saida/status/latest-status.json",
        "code_revision": code_revision,
        "status_stale": status_stale,
        "status_stale_sources": stale_sources,
        "status_reference_utc": status_reference_utc,
        "run": {
            "run_id": args.run_id,
            "run_dir": str(run_dir),
            "vm_order": args.vm_order.split(",") if args.vm_order else [],
            "cooldown_sec": int(args.cooldown_sec),
            "bootstrap_mode": args.bootstrap_mode,
            "manual_fallback_on_auto_fail": int(args.manual_fallback_on_auto_fail),
            "strict_signature": int(args.strict_signature),
            "signature_profile": args.signature_profile,
        },
        "summary": counts,
        "gates": {
            "g1": g1,
            "g2": g2,
            "g3": g3,
            "g4": g4,
            "details": {
                "g1_win10": g1_win10,
                "g1_win11": g1_win11,
                "g2_win10": g2_win10,
                "g2_win11": g2_win11,
            },
        },
        "transport_mode": {
            "per_vm": transport_modes,
            "overall": "iso" if transport_modes and all(v == "iso" for v in transport_modes.values()) else (
                "qga" if transport_modes and all(v == "qga" for v in transport_modes.values()) else "mixed"
            ),
        },
        "readiness": {
            "ready_for_real_test": ready_for_real_test,
            "blockers": blockers,
        },
        "checks": {
            "installer_contract_static_pass": contract_ok,
            "artifact_freshness_pass": artifact_fresh_ok,
            "artifact_freshness_output": freshness_output,
        },
        "evidence": {
            "csv": str(csv_file),
            "summary_md": str(summary_md),
            "gates_md": str(gates_md),
            "mandatory_csv": str(mandatory_csv),
        },
    }

    latest_json.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    md_lines = [
        "# Latest Status",
        "",
        f"- GeneratedUTC: `{payload['generated_at_utc']}`",
        f"- RunId: `{args.run_id}`",
        "- SourceOfTruth: `saida/status/latest-status.json`",
        f"- CodeRevision: `{code_revision['short_revision']}`",
        f"- CodeDirty: `{str(code_revision['dirty']).lower()}`",
        f"- StatusStale: `{str(status_stale).lower()}`",
        f"- StatusReferenceUTC: `{status_reference_utc}`",
        f"- SignatureProfile: `{args.signature_profile}`",
        f"- TransportMode: `{payload['transport_mode']['overall']}`",
        f"- ReadyForRealTest: `{str(ready_for_real_test).lower()}`",
        "",
        "## Gates",
        f"- G1: {g1}",
        f"- G2: {g2}",
        f"- G3: {g3}",
        f"- G4: {g4}",
        "",
        "## Totais",
        f"- PASS: {counts['PASS']}",
        f"- FAIL: {counts['FAIL']}",
        f"- PARCIAL: {counts['PARCIAL']}",
        f"- BLOQUEADO: {counts['BLOQUEADO']}",
        f"- TOTAL: {counts['TOTAL']}",
    ]

    if blockers:
        md_lines.append("")
        md_lines.append("## Blockers")
        for blocker in blockers:
            md_lines.append(f"- {blocker}")
    if stale_sources:
        md_lines.append("")
        md_lines.append("## Stale Sources")
        for source in stale_sources:
            md_lines.append(f"- {source}")

    latest_md.write_text("\n".join(md_lines) + "\n", encoding="utf-8")

    csv_lines = [
        "key,value",
        f"generated_at_utc,{payload['generated_at_utc']}",
        f"run_id,{args.run_id}",
        f"code_revision,{code_revision['short_revision']}",
        f"code_dirty,{str(code_revision['dirty']).lower()}",
        f"status_stale,{str(status_stale).lower()}",
        f"status_reference_utc,{status_reference_utc}",
        f"signature_profile,{args.signature_profile}",
        f"transport_mode,{payload['transport_mode']['overall']}",
        f"ready_for_real_test,{str(ready_for_real_test).lower()}",
        f"g1,{g1}",
        f"g2,{g2}",
        f"g3,{g3}",
        f"g4,{g4}",
        f"pass,{counts['PASS']}",
        f"fail,{counts['FAIL']}",
        f"parcial,{counts['PARCIAL']}",
        f"bloqueado,{counts['BLOQUEADO']}",
        f"total,{counts['TOTAL']}",
    ]
    latest_csv.write_text("\n".join(csv_lines) + "\n", encoding="utf-8")

    ux_day7_dir = installador_root / "saida" / "ux-dia7"
    ux_day7_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(latest_json, ux_day7_dir / f"status-{args.run_id}.json")
    shutil.copy2(latest_md, ux_day7_dir / f"status-{args.run_id}.md")
    shutil.copy2(latest_csv, ux_day7_dir / f"status-{args.run_id}.csv")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
