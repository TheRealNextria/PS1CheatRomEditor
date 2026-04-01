# 🎮 PS1 Cheat ROM Editor

A Windows Forms tool for editing PlayStation cheat cartridges such as:

- Xplorer / Xploder (including FX compressed ROMs)
- GameShark
- Equalizer

---

## ✨ Features

- 🧠 Full cheat database editor (games + cheats + codes)
- 🔍 Search and filter cheats instantly
- ➕ Add / remove games and cheats
- 📋 Paste multiple codes at once (auto parsing)
- 💾 Save patched ROMs
- 📦 Supports compressed Xplorer (FX) ROMs (decompress + recompress)
- 📊 Live cheat space calculation (used / free) (not working 100% yet)
- 🧾 Export cheat list to:
  - JSON
  - TXT (Xplorer format)

---

## 🔌 NOPS Integration

Built-in support for dumping and flashing via **NOPS (Unirom / PSXSerial)**

### Cartridge
- Dump ROM from cartridge
- Flash ROM to cartridge
- Detect cartridge size automatically (for some Xplorers)

### Memory Cards
- Dump Memory Card Slot 1 / 2
- Upload Memory Card to Slot 1 / 2

---

## ⚙️ Requirements

- Windows (x64)
- .NET 8 Runtime (if not self-contained build)
- `Tools` folder with:
  - `NOPS.exe`
  - `FxTokenDecoder.exe` (for FX ROMs)
  - `roms.json` / `GSroms.json / Equalizer.json`

---

## 🚀 Usage

1. Open a ROM file  
2. Edit games / cheats  
3. Add or modify codes  
4. Save patched ROM  

Optional:
- Dump or flash cartridges via NOPS
- Dump/upload memory cards

---

## ⚠️ Notes

- ROM rebuilding is accurate but still considered **experimental**
- Always keep a backup of original ROMs
- Some formats (especially FX compressed) rely on exact byte structure

---

## 🙏 Credits

- Unirom team – serial/NOPS support  
- lilkuz2005 (Discord) – icon  
