# REVIEW — Redesign Mobile: Simulasi Panel Surya 3D

> Dokumen ini untuk **review sebelum implementasi**. Belum ada file Unity yang diubah.
> Setelah Anda setuju, implementasi akan dieksekusi per fase (tiap fase diuji dulu).

---

## 1. Latar Belakang & Keputusan

Fokus aplikasi dialihkan ke **mobile (Android)** dengan syarat **PC/WebGL tetap berfungsi minim**.
Keputusan yang sudah disepakati:

| Aspek | Keputusan |
|---|---|
| Platform utama | **Android** (ARM64 sudah aktif di settings) |
| Kompatibilitas | Mobile + PC/WebGL tetap jalan |
| Orientasi layar | **Landscape** |
| Sistem input | **Migrasi penuh ke Input System (Actions)** — **user men-generate C# code** dari Input System; saya hanya **menyambungkan** (wire-up) |
| Penempatan furniture | Free drag-drop di dalam rumah (**gaya The Sims**) — Seret item, letakkan bebas (Fase 2) |
| Kabel | Wajib & strict — daya mengalir hanya bila rantai `Panel → Inverter → Baterai → Beban` benar; **cara sambung = ketuk logo listrik** pada beban (Fase 3) |

Urutan fase: **Fase 0 (fondasi mobile+input) → 1 (refactor) → 2 (placement) → 3 (kabel)**.

### 1.1 Kebutuhan spesifik dari user (harus dipenuhi)

1. **Interaksi kamera gaya The Sims (mobile):**
   - **Geser map dengan jari** — 1 jari menggeser pandangan (pan) di lantai.
   - **Rotasi** — putar kamera bebas; usulan: 2 jari geser telusur untuk memutar (yaw), plus **tombol rotasi di layar** sebagai cadangan (⇄ kiri/kanan).
   - **Zoom** — pinch 2 jari.
   - Prioritas utk touch: *pan paling intuitif* dengan 1 jari (bukan orbit).
2. **_Drag and drop_ furniture (gaya The Sims):** item dipilih dari panel kiri → muncul "ghost" di bawah jari → geser langsung ke tempat tujuan → putar (tombol/gestur) → lepas untuk menempatkan. Bisa digeser lagi kapan saja.
3. **Kabel inverter → semua barang elektronik, dengan logo listrik:**
   - Setiap barang elektronik/beban yang **sudah ditempatkan** otomatis muncul **logo listrik** di atasnya → artinya **"BELUM TERSAMBUNG"**.
   - **Cara menyambung: tinggal ketuk logonya** → kabel dari inverter (lewat rantai valid) otomatis tersambung ke barang itu.
   - Setelah tersambung, logo berubah status (misal hijau/"TERHUBUNG"); jika cabut/dipindah, logo kembali "belum tersambung".
   - Tetap **strict**: tidak bisa tersambung bila inverter/baterai/panel belum ada atau rantai tidak valid.

### 1.2 Workflow kerja (disepakati)

1. **User mengatur & men-generate C# code dari Unity Input System** (aset `.inputactions` + *Generate C# Class*) → hasil C# jadi milik user (bisa diedit sendiri).
2. **Saya hanya menyambungkan (wire-up)** kode hasil generate ke kamera, penempatan furniture, dan UI — **tidak** membuat ulang InputActionAsset manual.
3. Dikerjakan **per fase berurutan**: Fase 0 (**mulai dari mengubah Input System**) → 1 → 2 → 3 → QA Android.
4. **Aturan lanjut fase:** satu fase dianggap *done* hanya jika **compile tanpa error** (Editor & build Android) dan uji dasar lolos → baru lanjut ke fase berikutnya.
5. Jika error/blocker → **berhenti, perbaiki dulu**, laporkan ke user; jangan lanjut sebelum bersih.

---

## 2. Fakta yang sudah diverifikasi di project (masih berlaku)

- `Packages/manifest.json`: `com.unity.inputsystem` **1.18.0** terpasang; `activeInputHandler: 2` (keduanya aktif).
- URP 17.3 dengan dua asset: `PC_RPAsset` (guid `4b83569d…`, aktif sekarang) & `Mobile_RPAsset` (guid `5e6cbd92…`).
- Scene aktif di build: `Login.unity`, `Menu.unity`, `Simulasi.unity`.
  - EventSystem: Login = `StandaloneInputModule`; Menu & Simulasi = `InputSystemUIInputModule`.
- Kode yang masih memakai API Input lama (`Input.*`):

| File | Lokasi | Isi lama |
|---|---|---|
| `OrbitCamera.cs` | baris 104–161 | orbit/zoom/pan/WASD via `Input.*` |
| `SolarPanelInstaller.cs` | 973, 1006, 1027, 1039 | `mousePosition` / `GetMouseButtonDown(0)` |
| `BatteryInstaller.cs` | 169, 177, 219, 768 | sama |
| `UIDeselectManager.cs` | 8 | `GetMouseButtonUp(0)` |
| `LoginController.cs` | 65, 76 | `GetKeyDown(Tab)` / `GetKey(Shift)` |

- `RoomFocusManager.cs` punya `ForceEnterFreeMode()` / `ForceExitFreeMode()` (dipanggil `SimulationManager`).
- Tidak ada `transform.Find` berbahaya untuk reparenting seluruh canvas (SimulationManager memakai `canvas.transform.Find("UI Right")` dll — jadi **tidak boleh** reparent root canvas; safe-area dibuat tanpa reparent).
- Folder `Assets/Scripts/Core/` belum ada (akan dibuat di fase 0).

---

## 3. Fase 0 — Fondasi Mobile & Migrasi Input System

> **Langkah pertama fase ini = mengubah/menyetel Input System.** User membuat aset `.inputactions` & men-generate C# code-nya di Unity; saya tinggal menyambungkan kode hasil generate ke kamera, placement, dan UI.

### 3.1 File baru (folder `Assets/Scripts/Core/Input/`)

**`GameInput.cs`** (wrapper statik di atas C# hasil generate user — bukan InputActionAsset manual)
- User: aset `.inputactions` (action map **Camera** & **Interaction**) + *Generate C# Class* (mis. `GameInputActions`).
- Saya: `GameInput.cs` menjadi **static wrapper** yang menginisialisasi instance actions hasil generate, plus helper:
  - `PointerPosition` (mouse/sentuh), `PrimaryPressed()` (mouse = press; sentuh = tap tanpa drag), `IsPointerOverGameObject(pos)` (via EventSystem RaycastAll — berlaku mouse DAN sentuh).
- Action map **Camera**: `Orbit` & `OrbitHold` (delta + klik kanan), `Pan` & `PanHold` (klik tengah), `Zoom` (scroll), `Move` (composite WASD/arrow), `MoveY` (Axis: Space/Ctrl), `Sprint` (Shift).
- Action map **Interaction**: `Primary`, `PrimaryPosition`, `RotateLeft` (Q), `RotateRight` (E), `Cancel` (Esc), `Tab`, `Shift`.

**`TouchGestureController.cs`** (bootstrap otomatis tiap scene, tanpa edit scene)

| Gestur (EnhancedTouch) | Hasil (dibaca kamera/placement) |
|---|---|
| 1 jari geser | `DragDelta` → **geser map (pan)** — gaya The Sims |
| 2 jari geser searah sumbu mendatar | `RotateDelta` → **rotasi kamera (yaw)** |
| 2 jari pinch (jarak antar jari berubah) | `PinchDelta` → zoom |
| ketuk (< 20px, tanpa drag) | `TappedThisFrame` |
| sentuhan mulai di atas UI | diabaikan (cek via `IsPointerOverGameObject`) |

**`VirtualJoystick.cs`** — pengganti WASD di mobile
- Dibuat programatik (Canvas overlay + lingkaran), tanpa edit scene; **hanya muncul saat free mode**.
- Knob planar + tombol ▲/▼ untuk naik/turun. Nilai gabung keyboard di kamera.

**`ScreenSafeArea.cs`** (folder `Assets/Scripts/Core/UI/`)
- Di-attach otomatis ke root canvas saat scene dimuat (hanya `Application.isMobilePlatform`).
- Tidak reparent anak canvas (mengubah anchor root canvas sesuai `Screen.safeArea`).

**`Editor/BuildPlatformConfig.cs`** — saat build Android → `GraphicsSettings.defaultRenderPipeline = Mobile_RPAsset`; selain itu PC_RPAsset; dipulihkan setelah build.

### 3.2 File diubah (Fase 0)

| File | Perubahan |
|---|---|
| `OrbitCamera.cs` | Hapus semua `Input.*`; baca `GameInput` + `TouchGestureController` + `VirtualJoystick`. Field lama `flyKey/downKey` dihapus (digabung ke binding Space/Ctrl). `inputLocked` tetap menghalangi semua input kamera. |
| `SolarPanelInstaller.cs` | `Input.mousePosition`→`GameInput.PointerPosition`; `GetMouseButtonDown(0)`→`GameInput.PrimaryPressed()`. |
| `BatteryInstaller.cs` | Sama (empat titik). |
| `UIDeselectManager.cs` | `GetMouseButtonUp(0)`→`GameInput.Primary.WasReleasedThisFrame()`. |
| `LoginController.cs` | Tab→`GameInput.Tab`, Shift→`GameInput.Shift`. |
| `RoomFocusManager.cs` | Free mode → `VirtualJoystick.SetVisible(...)` (di mobile saja). |
| `Login.unity` | EventSystem: `StandaloneInputModule` → `InputSystemUIInputModule` (salin persis blok dari `Menu.unity`: guid script `01614664…`, aset default actions `ca9f5fa9…`). |
| `ProjectSettings.asset` | `activeInputHandler: 2 → 1`; `defaultScreenOrientation: 4 → 3` (landscape). |
| `plan.md` | Versi final (dokumen ini). |

### 3.3 Perilaku kontrol (target akhir Fase 0)

| Tindakan | PC/WebGL | Android |
|---|---|---|
| **Geser map (pan)** | Klik tengah + drag | **1 jari geser** (gaya The Sims) |
| **Putar kamera (rotasi)** | Klik kanan + drag | 2 jari geser + tombol rotasi layar (⇄) usulan |
| Zoom | Scroll wheel | Pinch 2 jari |
| Gerak bebas | WASD + Spasi/Ctrl (+Shift cepat) | Virtual joystick + ▲/▼ |
| Konfirmasi/klik | Klik kiri | Ketuk |
| Navigasi field | Tab / Shift+Tab | Ketuk field (keyboard OS otomatis) |

> Catatan: mapping sentuh di revisi ini **mengganti** rencana awal "1-jari = orbit" menjadi **"1-jari = geser map"** sesuai permintaan gaya The Sims. Sensitivitas & ambang gerak (pan vs rotasi vs tap) akan disetel saat pengujian Fase 0.

---

## 4. Fase 1 — Refactor Arsitektur (setelah Fase 0 disetujui & diuji)

- Buat `Assets/Scripts/Core/`: `PowerNode`, `PowerConnection`, `PowerGrid`.
- Ganti refleksi `SolarPanelInstaller.ConfigureSolarPanel` (sekitar baris 1109) → public `SolarPanel.Configure(...)`.
- Ganti refleksi `BatteryInstaller.SetOrbitField` → public API `OrbitCamera`.
- `FurnitureManager.AddFurniture(data, pos, rot)` — posisi runtime; `fixedPosition` di asset jadi default saja.
- Uji: compile bersih; penempatan lama tetap bekerja; uji sentuh Android.

## 5. Fase 2 — Free Drag-Drop Furniture (gaya The Sims, touch-first)

- `FurniturePlacementController.cs`: placement mode + ghost transparan (mengikuti jari/kursor).
- **Alur drag-drop (The Sims):** pilih item dari panel kiri → ghost muncul di bawah jari → geser langsung ke titik tujuan (raycast lantai) → putar (tombol ⇄ di layar + R/Q/E) → **lepas untuk menempatkan**. Item bisa digeser/dipindah lagi kapan saja; `Esc`/back batal.
- Validasi: collider lantai per ruangan, anti-overlap, batas bounds ruangan.
- Integrasi kartu "+ Masukan" → drag-drop → `AddFurniture(data, pos, rot)` → `FurnitureConfigPanel`.
- Ikon `FurniturePowered` (+ **logo listrik** sambungan kabel, lihat Fase 3) mengikuti posisi baru.

## 6. Fase 3 — Sistem Kabel (strict, sambung dengan ketuk logo)

- `WiringManager.cs` (graph node+koneksi, evaluasi rantai `Panel → Inverter → Baterai → Beban`).
- `ConnectorNode.cs` (socket) + **`PowerLogo`** — logo listrik di atas setiap barang elektronik yang ditempatkan.
- **Alur sambung (permintaan user):**
  1. Barang elektronik ditempatkan → muncul **logo listrik "BELUM TERSAMBUNG"** (misal warna abu/kuning) di atasnya.
  2. **Ketuk logonya** → `WiringManager` memvalidasi rantai; bila valid → kabel dari inverter otomatis ditarik ke barang tersebut (`Cable3D` spline + efek sag).
  3. Logo berubah menjadi **"TERHUBUNG"** (misal hijau). Barang dipindah/dihapus → kabel ikut lepas & logo kembali "belum tersambung".
  4. Strict: ketuk logo tidak bisa tersambung bila inverter/baterai/panel belum terpasang atau rantai tidak valid — muncul peringatan.
- Integrasi ke `BatteryManager`: daya panel hanya mengalir lewat rantai valid; furnitur **menyala hanya jika tersambung** (logo hijau).
- `ObjectiveManager`: toggle "Kabel Terpasang" wajib sebelum simulasi (semua beban tersambung).

---

## 7. Risiko & Catatan

1. **activeInputHandler = 1** memicu `InvalidOperationException` bila ada kode lama `Input.*` tersisa → dilakukan bersama pembersihan total; diverifikasi via build.
2. **Enhance Touch vs UI**: 1-jari geser (pan) dapat berbenturan dengan tap/gestur placement → pemisahan: tap = release tanpa drag (dengan ambang jarak), dan sentuhan mulai di atas UI diabaikan; rotasi 2-jari dibedakan dari pinch via ambang gerak.
3. **Simulasi.unity ±48k baris** → semua perubahan component-level, bukan rewrite.
4. Banyak auto-find berbasis nama-string (`SimulationManager.cs:72` dst) → dirapikan bertahap, dipertahankan sampai penggantinya lolos uji.
5. Scene cadangan (`Login 1.unity`, `Menu 1.unity`, `* backup`) **tidak diubah** — hanya scene aktif build.
6. `VirtualJoystick` dibuat runtime → tidak menambah GameObject permanen ke scene.
7. **Baseline performa** dicatat sebelum & sesudah tiap fase (Android build praktis).
8. **Logo listrik (PowerLogo)** = World-Space UI yang mengikuti objek → dijaga posisi di atas pivot tiap beban, tidak menabrak raycast placement, dan mati saat objek dipindah/dihapus.

---

## 8. Rencana Pengujian per Fase

| Fase | Kriteria lolos |
|---|---|
| 0 | Editor PC: pan/rotasi/zoom/WASD + UI klik normal; Android: geser map 1 jari, rotasi 2 jari, pinch zoom, joystick, tap, tanpa crash; Console bersih; build Android sukses. |
| 1 | Compile bersih; penempatan lama normal; refleksi hilang. |
| 2 | Drag-drop gaya The Sims: letakkan/pindah/putar/hapus furniture di Editor & Android; ghost mengikuti jari; collision & bounds benar. |
| 3 | Beban menampilkan logo "belum tersambung"; **ketuk logo → kabel inverter tersambung** melawan rantai valid; bila rantai salah → tidak tersambung (warning); furnitur menyala hanya jika tersambung; simulasi diblokir tanpa kabel lengkap. |

---

**Checklist persetujuan (Anda)**
- [ ] Setuju Fase 0 (input + mobile) terlebih dahulu — **dimulai dengan user generate C# Input System, saya menyambungkan**
- [ ] Setuju workflow: user generate C# dari Input System; saya **hanya menyambungkan**; per fase, lanjut hanya bila **tidak error**
- [ ] Setuju `activeInputHandler` → 1 & orientasi landscape
- [ ] Setuju gestur gaya The Sims: **1 jari = geser map**, 2 jari = rotasi, pinch = zoom, ketuk = konfirmasi
- [ ] Setuju alur sambung kabel: **barang elektronik muncul logo listrik "belum tersambung", sambung dengan mengetuk logo**
- [ ] Lain-lain / catatan dari reviewer: ______________________