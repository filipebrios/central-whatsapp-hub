"""Aplica SOMENTE o encaixe Agenda ao MODUX 1.6.1 durante o build da candidata 1.7.
Não modifica o WhatsApp, perfis, dados nem extensões. Idempotente.
"""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "webview2-prototype" / "MainForm.cs"
s = path.read_text(encoding="utf-8-sig")
if "ConfigureAgendaFeature();" in s:
    print("Agenda já encaixada.")
    raise SystemExit(0)

edits = [
    ("public sealed class MainForm : Form", "public sealed partial class MainForm : Form"),
    (
        "        sidebar.Controls.Add(accountsPanel);\n",
        "        sidebar.Controls.Add(accountsPanel);\n"
        "        sidebar.Controls.Add(agendaShortcutPanel);\n",
    ),
    (
        "        Controls.Add(rightPanel);\n        Controls.Add(sidebar);\n",
        "        Controls.Add(rightPanel);\n        Controls.Add(sidebar);\n"
        "        ConfigureAgendaFeature();\n",
    ),
    (
        "        browser.Visible = true;\n        browser.BringToFront();\n"
        "        UpdateActiveAccountHeader();\n",
        "        browser.Visible = true;\n        browser.BringToFront();\n"
        "        ShowWhatsAppFeature();\n"
        "        UpdateActiveAccountHeader();\n",
    ),
]
for before, after in edits:
    if s.count(before) != 1:
        raise SystemExit("FALHA: versão inesperada MainForm.cs: " + repr(before[:60]))
    s = s.replace(before, after, 1)

project = root / "webview2-prototype" / "CentralWhatsApp.WebView2.csproj"
p = project.read_text(encoding="utf-8")
installer = root / "webview2-prototype" / "CentralWhatsApp.iss"
i = installer.read_text(encoding="utf-8")
assert p.count("<Version>1.6.1</Version>") == 1, "csproj version wrong"
assert i.count('#define MyAppVersion "1.6.1"') == 1, "Inno version wrong"

path.write_text(s, encoding="utf-8")
project.write_text(p.replace("<Version>1.6.1</Version>", "<Version>1.8.1</Version>", 1), encoding="utf-8")
installer.write_text(i.replace('#define MyAppVersion "1.6.1"', '#define MyAppVersion "1.8.1"', 1), encoding="utf-8")
print("Encaixe Agenda São Bento v1.8.1 aplicado com sucesso")
