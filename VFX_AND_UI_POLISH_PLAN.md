# VFX & UI Polish Uygulama Planı (Dream / Peak / Good Job Standartları)

> **Not:** Bu döküman, **Gemini Flash High** modelinin hiçbir harici ücretli asset'e ihtiyaç duymadan, tamamen prosedürel yüksek çözünürlüklü Sprite/Texture üretimi ve **DOTween** koreografisiyle kodu doğrudan uygulayabilmesi için adım adım teknik spesifikasyon olarak hazırlanmıştır.

---

## 1. Mevcut Kod Tabanı Analizi ve Kök Nedenler

1. **Kova `%100` Olduğunda VFX Tetiklenmiyor:**
   - `Assets/_Project/Scripts/Gameplay/VFX/JuicyVFXManager.cs` içinde `PlayBucketCompleteVFX(Vector3 bucketWorldPos, Color32 bucketColor)` metodu mevcut olmasına rağmen, `Assets/_Project/Scripts/Gameplay/Containers/DockManager.cs` içindeki `NotifyGrainArrivedAtSlot` metodunda `bucketCompleted == true` olduğunda **hiç çağrılmıyor**.
2. **Partikül ve Shockwave Görselleri Düz Kare (Graybox):**
   - `JuicyVFXManager.cs` içindeki `_sparkleParticleSystem` ve `_shockwaveMaterial` herhangi bir alfa dokusu (radial ring, 4-köşeli yıldız, yumuşak daire) kullanmadığı için ekranda sert kenarlı kareler (`PrimitiveType.Quad`) olarak görünüyor.
3. **Win ve Fail Panelleri Çok Basit (Graybox):**
   - `Assets/_Project/Scripts/UI/UIManager.cs` içindeki `OnLevelWon` ve `OnLevelDeadlocked` yalnızca mevcut düz paneli `SetActive(true)` yapıp `DOScale` uyguluyor. Arka plan karartma (Dimmer), dönen ışık huzmesi (Sunburst), 3D katmanlı kart çerçevesi, kurdele başlık (Ribbon), kademeli yıldız vuruşu (Staggered Star Slam), konfeti topları veya kaybetme anı "Near-Miss" ilerleme özeti bulunmuyor.

---

## 2. Adım Adım Uygulama Spesifikasyonu (Gemini Flash High İçin)

### ADIM 1: `JuicyVFXManager.cs` Yenilenmesi (Prosedürel Dokular & Çok Katmanlı VFX)

**Dosya:** `Assets/_Project/Scripts/Gameplay/VFX/JuicyVFXManager.cs`

#### 1.1. Bellekte Prosedürel Anti-Aliased Texture Üretimi (`Awake` içinde bir kez üretilir, Zero-GC)
Düz kare görünümünü tamamen yok etmek için `64x64` veya `128x128` çözünürlükte `Texture2D` (`RGBA32`) olarak şu 4 doku prosedürel üretilmeli:
1. **4-Point Star Texture (`CreateStarTexture(64)`):**
   - Merkezden `(0.5, 0.5)` yatay ve dikey eksenlerde keskinleşen, köşegenlerde içbükey daralan (`pow(1 - abs(dx), 4) + pow(1 - abs(dy), 4)` + merkezde yumuşak dairesel çekirdek) parlak yıldız dokusu.
2. **Soft Circle / Droplet Texture (`CreateSoftCircleTexture(64)`):**
   - Merkezden dışa `smoothstep(1.0, 0.0, dist)` ile yumuşak eriyen dairesel damlacık/glow dokusu.
3. **Radial Shockwave Ring Texture (`CreateRingTexture(128)`):**
   - Yarıçapı `r = 0.72`, kalınlığı `0.14` olan içi boş, kenarları yumuşak geçişli (`smoothstep`) halka dokusu.
4. **Confetti Flake Texture (`CreateConfettiTexture(32)`):**
   - Hafif yuvarlatılmış dikdörtgen konfeti parçası dokusu.

*Shader Notu:* URP / Standard uyumluluğu için partikül ve shockwave materyallerinde `Universal Render Pipeline/Particles/Unlit` (yoksa `Particles/Standard Unlit` veya `Sprites/Default`) shader'ı kullanılmalı ve `Surface Type = Transparent` / `BlendMode = Alpha` (veya yıldızlar için `Additive`) ayarlanmalıdır.

#### 1.2. Eklenecek / Güncellenecek VFX Metodları
1. **`PlayGrainSplashMicroVFX(Vector3 rimWorldPos, Color32 grainColor)`:**
   - Kova kum alırken her birkaç tanede bir (cooldown `0.06s`) kova ağzında `2-3` adet küçük (`size: 0.06..0.11`, `lifetime: 0.18..0.28s`) yumuşak dairesel kum damlacığı sıçratır.
2. **`PlayBucketCompleteVFX(Vector3 bucketWorldPos, Color32 bucketColor)` (Güncelleme):**
   - **Alt-Sistem A (Starburst):** `8-10` adet 4-köşeli parlak yıldız (`Color.Lerp(bucketColor, Color.white, 0.65f)`), dönerek (`angularVelocity = 180..360`) dışa fırlar.
   - **Alt-Sistem B (Color Droplets):** `16-20` adet kova renginde (`bucketColor` ve `%30` açık tonu) yuvarlak damlacık yukarı doğru konik fırlar ve yerçekimiyle (`gravityModifier = 2.6f`) dökülür.
   - **Alt-Sistem C (Soft Shockwave Ring):** `RingTexture` kullanan quad `0.15 -> 1.55` ölçeğe `0.28s` (`Ease.OutCubic`) içinde büyürken alfa değeri `0.95 -> 0.0` olarak sönümlenir (`DOFade` / `MaterialPropertyBlock`).
   - **Alt-Sistem D (Rising Pop Text / Icon):** Kovanın tam üstünde kısa süreli (`0.45s`) yukarı süzülen altın/beyaz `"★ PERFECT!"` veya `"✓ 100%"` 3D `TextMeshPro` pop-up'ı (havuzlanmış - pooled).
   - **Kamera Mikro-Punch:** `TriggerCameraMicroPunch(0.045f, 0.14f)`.
3. **`PlayWinConfettiCannons()` (Yeni):**
   - Kamera görüş alanının sol-alt ve sağ-alt köşelerinden ekran ortasına/yukarısına doğru `60-80` adet çok renkli (Altın, Pembe, Turkuaz, Yeşil, Turuncu, Mor) dönen konfeti ve yıldız partikülü fırlatır.
4. **`PlayFailDeadlockVFX(IReadOnlyList<Vector3> stuckBucketPositions)` (Yeni):**
   - Konveyördeki takılı kalan kovaların üzerinde gri-mor duman pufçukları (`SoftCircleTexture`, düşük hız, yukarı süzülerek büyüyen) çıkarır ve kameraya kısa bir yatay "tıkandı" titremesi (`DOShakePosition`) uygular.

---

### ADIM 2: `DockManager.cs` ve `BucketView.cs` Entegrasyonu

**Dosyalar:**
- `Assets/_Project/Scripts/Gameplay/Containers/DockManager.cs`
- `Assets/_Project/Scripts/Gameplay/Containers/BucketView.cs`

#### 2.1. `DockManager.NotifyGrainArrivedAtSlot` Güncellemesi (Satır 647-690)
- Kova kum aldığında (`!bucketCompleted` iken):
  - `view.UpdateFillProgress(...)` çağrısına ek olarak `JuicyVFXManager.Instance?.PlayGrainSplashMicroVFX(GetSlotIntakeWorldPosition(slotIndex), view.BucketColor)` tetiklenir.
- Kova `%100` olduğunda (`bucketCompleted == true` iken):
  - Kovanın o anki dünya pozisyonu (`Vector3 fxPos = view != null ? view.CachedTransform.position : GetSlotBucketWorldPosition(slotIndex)`) ve rengi alınarak **`JuicyVFXManager.Instance?.PlayBucketCompleteVFX(fxPos, view.BucketColor)`** çağrılır!

#### 2.2. `BucketView.cs` İçinde Dolum ve Tamamlanma Juiciness'ı
- **`BucketColor` Property'si:** `public Color32 BucketColor => _bucketColor;` dışarıya açılır.
- **`UpdateFillProgress` (Satır 523-547):**
  - Kum yüzeyi punch'ına ek olarak `_bucketBodyTransform` üzerinde hafif bir **Squash & Stretch** (`DOPunchScale(new Vector3(0.035f, 0.035f, -0.025f), 0.09f, 2, 0.4f)`) uygulanır.
  - Doluluk `%80` üzerindeyse `_capacityText` hafif altın sarısına (`#FFE066`) çalar.
- **`AnimateCompletionAndRelease` (Satır 955-971):**
  - `TriggerHoloSheenFlash(1.0f, 0.22f)` çağrılarak kova gövdesine parlak beyaz/altın rim flash vurulur.
  - `_capacityText` metni `"✓"` (veya `"100% ★"`) yapılır ve altın rengine döner.
  - Kova `0.12s` içinde `1.26x` ölçeğe şişer (`Ease.OutBack`), ardından yukarı doğru zıplarken (`DOMoveY(+0.55f, 0.22f)`) kendi etrafında `180°` Y-ekseni dönüşü (`DORotate`) yaparak `Vector3.zero` ölçeğine küçülür (`Ease.InBack`).

---

### ADIM 3: `UIManager.cs` — Dream / Peak / Good Job Tarzı Win & Fail Panelleri

**Dosya:** `Assets/_Project/Scripts/UI/UIManager.cs`

*Önemli:* Sahnedeki mevcut `_winPanel`, `_failPanel`, `_winNextLevelButton` ve `_failRetryButton` referansları korunacak; `UIManager` başlangıçta (`Awake`/`Start`) bu panelleri otomatik olarak **Dream / Peak tarzı çok katmanlı prosedürel UI hiyerarşisine** dönüştürecek (veya eksik alt elemanları oluşturacak). Böylece sahne dosyasını elle düzenlemeye gerek kalmadan tek tıkla çalışacaktır!

#### 3.1. Prosedürel UI Görsel Elemanları (Runtime 9-Slice & Radial Sprite Üretimi)
`UIManager` içinde üretilecek UI Sprite'ları:
1. **`CreateRoundedRectSprite(int w, int h, int radius)`:** 9-slice destekli pürüzsüz yuvarlatılmış dikdörtgen (Kart gövdesi, 3D alt gölge dudak katmanı, Kurdele başlık ve 3D Butonlar için).
2. **`CreateSunburstSprite(int size, int rayCount = 12)`:** Merkezden dışa yumuşak sönümlenen 12 kollu dönen ışık huzmesi (God Rays) dokusu.
3. **`CreateStarIconSprite(int size)`:** Dolgun, parlak 5-köşeli altın yıldız ikonu (Win panelindeki 3 yıldız için) ve koyu yıldız yuvası (empty star slot).

#### 3.2. Win Paneli Koreografisi (`OnLevelWon`)
1. **T = `0.00s` (Konfeti Patlaması):**
   - `JuicyVFXManager.Instance?.PlayWinConfettiCannons()` tetiklenir.
2. **T = `0.28s` (Dimmer & Sunburst):**
   - Tam ekran koyu lacivert-siyah `DimmerOverlay` (`rgba(10, 14, 28, 0.76)`) `0.25s` içinde `DOFade` ile belirir.
   - Arkadaki altın `SunburstImage` (`#FFD95A`, `Alpha: 0.42`) ölçeklenerek açılır ve sürekli `DORotate(new Vector3(0, 0, -360f), 18f, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear)` ile döner.
3. **T = `0.35s` (3D Kart & Kurdele Başlık):**
   - **Kart Gövdesi:** Krem-beyaz (`#FFFDF7`) iç yüzey, kalın mavi-altın dış çerçeve (`#2B6CB0` / `#F6AD55`) ve altında `8px` koyu 3D derinlik katmanı. `Scale 0.5 -> 1.0` (`0.32s`, `Ease.OutBack`) ile ekrana oturur.
   - **Üst Kurdele (Ribbon):** Kartın üst kenarına binen parlak altın/yeşil banner üzerinde kalın konturlu **"LEVEL COMPLETED!"** ve altında `"LEVEL X CLEARED"` alt başlığı.
4. **T = `0.55s .. 0.80s` (3 Yıldız Staggered Slam):**
   - Ortadaki 3 yıldız yuvasına (`Star_Left`, `Star_Center_Big`, `Star_Right`) sırayla `0.12s` arayla yıldızlar `Scale 2.4 -> 1.0` (`Ease.OutBack`) ve hafif rotasyonla vurarak oturur.
   - Her yıldız oturduğunda kart hafifçe sarsılır (`DOPunchScale(Vector3.one * 0.03f, 0.10f)`).
5. **T = `0.85s` (Nefes Alan 3D "CONTINUE / NEXT" Butonu):**
   - Altında koyu yeşil 3D kalınlık katmanı (`#1E8449`) bulunan parlak yeşil (`#2ECC71`) yuvarlatılmış buton `Scale 0 -> 1` (`Ease.OutBack`) ile fırlar.
   - Buton aktifken sürekli yumuşak bir nefes alma (`DOScale(1.05f, 0.65f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine)`) animasyonu yapar.

#### 3.3. Fail Paneli Koreografisi (`OnLevelDeadlocked`)
1. **T = `0.00s` (Deadlock Oyun İçi Uyarı):**
   - `JuicyVFXManager.Instance?.PlayFailDeadlockVFX(...)` çağrılır; konveyördeki kovalar titrer ve duman çıkarır.
   - Ekran kenarlarında kırmızımsı bir uyarı kararması (`Red Vignette`) hafifçe yanıp söner.
2. **T = `0.35s` (Dimmer & 3D Fail Kartı):**
   - Tam ekran koyu mor-lacivert `DimmerOverlay` (`rgba(14, 10, 24, 0.80)`) `DOFade` ile açılır.
   - Kart gövdesi `Ease.OutBack` ile açılır ve hemen ardından kısa bir yatay "reddetme/hata" titremesi (`DOShakeAnchorPos(0.25f, new Vector2(18f, 0f), 18)`) yapar.
3. **Kurdele Başlık & Merkez "Near-Miss" İlerleme Gösterimi:**
   - **Üst Kurdele:** Mercan Kırmızısı (`#FF4757` / Alt gölge `#C0392B`) üzerinde **"NO MORE SPACE!"** başlığı.
   - **Merkez İkon & Near-Miss Barı:**
     - Ortada büyük, dikkat çekici bir **"!" / Taşmış Kova** rozeti (`DOPunchRotation` ile hafif yana eğilen).
     - Altında oyuncuyu motive eden **Near-Miss İlerleme Kutusu**:
       - *"Seviyenin **`%XX`'ini** tamamladın! Çok yaklaştın!"* metni ve `0%'dan %XX'e` `0.5s` içinde dolan (`DOFillAmount`) parlak turuncu-altın ilerleme çubuğu (`_totalLevelGrains` ve `evt.RemainingGrains` üzerinden hesaplanır).
4. **T = `0.65s` (3D "TRY AGAIN" Butonu):**
   - Parlak Turuncu/Yeşil (`#FF9F43` veya `#2ECC71`) 3D katmanlı **"TRY AGAIN"** butonu zıplayarak açılır ve idle pulse animasyonuna girer.

---

## 3. Renk Paleti Referansı (Dream / Peak UI & VFX)

| Eleman | Ana Renk (Hex) | Gölge / 3D Dudak (Hex) | Vurgu / Text (Hex) |
| :--- | :--- | :--- | :--- |
| **Kart Gövdesi (Card Body)** | `#FFF9F0` (Sıcak Krem) | `#D5CBB8` (Alt Kalınlık) | `#2C3E50` (Koyu Lacivert Yazı) |
| **Win Ribbon Banner** | `#FFB800` (Kraliyet Altını) | `#CC8800` (Koyu Altın) | `#FFFFFF` + `#5C3800` Outline |
| **Fail Ribbon Banner** | `#FF4757` (Mercan Kırmızı) | `#B32432` (Koyu Kırmızı) | `#FFFFFF` + `#4A0E13` Outline |
| **Win / Retry CTA Butonu** | `#2ECC71` (Canlı Yeşil) | `#1E8449` (3D Alt Yeşil) | `#FFFFFF` + `#0E4B26` Outline |
| **Yıldız & Parıltı VFX** | `#FFE066` (Parlak Yıldız) | `#FFA800` (Altın Çekirdek) | `#FFFFFF` (Merkez Parlama) |

---

## 4. Doğrulama ve Test Kriterleri (Checklist)

- [ ] **Kova `%100` Testi:** Kova dolduğu an `DockManager` üzerinden `JuicyVFXManager.PlayBucketCompleteVFX` tetikleniyor mu? Kare partikül yerine yıldız, yuvarlak damlacık ve yumuşak halka (shockwave) çıkıyor mu?
- [ ] **Kova Çıkış Animasyonu:** Kova `%100` olduğunda beyaz parlama (HoloSheen), `✓` ikonu ve `180°` dönüşlü yukarı zıplama yapıyor mu?
- [ ] **Win Paneli Testi:** Seviye bittiğinde önce konfetiler patlayıp ardından Dimmer, dönen Sunburst, 3D Kart, sırayla vuran 3 Yıldız ve nefes alan yeşil buton sorunsuz açılıyor mu? `"NEXT LEVEL"` butonu sonraki seviyeyi yüklüyor mu?
- [ ] **Fail Paneli Testi:** Konveyör kilitlendiğinde kovalarda titreme/duman çıkıp, ardından Fail kartı üzerinde oyuncunun o ana kadar topladığı kum yüzdesi (`%XX`) animasyonlu bar ile gösteriliyor mu? `"TRY AGAIN"` butonu seviyeyi sıfırlıyor mu?
- [ ] **Performans & Bellek (Zero-GC):** Tüm prosedürel texture ve UI elemanları yalnızca bir kez oluşturulup önbelleğe (cache) alınıyor mu? Oyun içi `60 FPS` korunuyor mu?

