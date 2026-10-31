# Pixel & Sand Sort Puzzle - Teknik Mimari ve AI Geliştirme Dokümanı

> **Doküman Amacı:** Bu dosya, görseldeki kum/boncuk tanelerini alt kısımdaki kapasiteli kovalara çekme (Color Extraction & Dock Management Puzzle) oyununun hem geliştirici takibi hem de **Antigravity IDE AI Asistanına** mimari sınırları, veri yapılarını ve algoritmaları öğretmek için hazırlanmış merkezi tasarım dokümanıdır (Single Source of Truth).
> **Hedef Platformlar:** Mobile (iOS/Android) & **WebGL (Portfolyo/CV Sunumu için Tarayıcıda Oynanabilir Build)**.

---

## 1. Mimari Vizyon ve Mühendislik Prensipleri (CV ve Stüdyo Odaklı)

Dream Games, Peak, Good Job Games veya Rollic gibi stüdyoların teknik mülakatlarında değerlendirilen temel unsur; binlerce parçacığın aktığı bir bulmaca oyununda bellek yönetimi (Memory Management), deterministik veri yapıları ve otomatik seviye üretim araçlarıdır (Custom Tooling). AI asistanı kod üretirken aşağıdaki kurallara **kesinlikle** uymalıdır:

1. **Tekil Sorumluluk Prensibi (SRP) & Pure C# Simülasyon Çekirdeği:**
   * Oyun kuralları, matris yerçekimi ve kova/dock matematiği `UnityEngine.MonoBehaviour`'dan tamamen bağımsız **Saf C# (Pure C#)** simülasyon katmanında (`SandSort.Simulation`) çalışmalıdır. Bu sayede hem Editör içindeki **Headless Bot** saniyede $1000$ oyunu görselsiz simüle edebilir hem de çalışma zamanındaki (`Runtime`) görsel yöneticiler birebir aynı deterministik motoru kullanır.
2. **Sıfır Çöp Üretimi (Zero-GC Allocation):**
   * Kum taneleri matristen kovalara akarken `Update` döngüsünde kesinlikle `Instantiate`, `Destroy`, `LINQ` sorguları, `foreach` boxing veya `new` ile referans tipli nesne üretimi yapılmamalıdır. Önceden ayrılmış (pre-allocated) diziler, `struct` veri modelleri ve `ObjectPool` kullanılmalıdır.
3. **Veri ile Görselin Ayrılması (Data-Visual Decoupling) & Staggered Juice:**
   * Çekilen kumların kova kapasitesini doldurması matematiksel olarak anında rezerve edilmeli; ekranda uçan kum taneleri yalnızca bu veriyi takip eden görsel birer illüzyon olmalıdır.
   * Oyuncu üst üste hızlı tıkladığında görsel gecikme yüzünden veri kaybı veya fazladan kum çekilmesi (Race Condition) yaşanmamalıdır.
   * Görsel tatmin (Game Feel) için rezerve edilen kumlar aynı karede (frame) üst üste fırlatılmak yerine, `StaggerInterval` (örn. her $0.025\text{ sn}$'de bir) kuyruğu ile ardışık vakumlanmalıdır.
4. **Olay Güdümlü İletişim (Event-Driven Architecture):**
   * Oyun durumu, kova doluluk yüzdeleri ($0\% \to 100\%$) ve bölüm sonu/kilitlenme (Deadlock) bildirimleri tip güvenli (struct tabanlı, Zero-GC) `EventBus` üzerinden yayınlanmalıdır.
5. **WebGL & Mobil Uyumluluk:**
   * Tarayıcı üzerinde (Single-Threaded WebGL ortamında) takılma olmaması için ağır `System.Threading` kilitleri yerine deterministik frame-bütçeli güncellemeler ve WebGL 2.0 uyumlu GPU Instancing / `MaterialPropertyBlock` teknikleri kullanılmalıdır.

---

## 2. Proje Klasör ve İsim Uzayı (Namespace & Assembly) Yapısı

AI asistanı yeni bir C# betiği oluştururken aşağıdaki klasör ve `.asmdef` (Assembly Definition) hiyerarşisini korumalıdır:

* `Assets/_Project/Scripts/Core/` -> `SandSort.Core` (`EventBus`, `ObjectPool<T>`, Sabitler, `ServiceLocator`)
* `Assets/_Project/Scripts/Data/` -> `SandSort.Data` (`LevelDataSO`, `BucketData`, `CellData` ve Matris veri yapıları)
* `Assets/_Project/Scripts/Simulation/` -> `SandSort.Simulation` (**Pure C#** deterministik matris yerçekimi, açık yüzey tespiti, Dock/Kova rezervasyonu ve Deadlock kuralları — Bot ve Runtime ortak kullanır)
* `Assets/_Project/Scripts/Editor/` -> `SandSort.EditorTools` (PNG'den seviye üreten `EditorWindow`, Tersine Dağıtım algoritması, Kalan Piksel Dengeleyici ve Çözülebilirlik Simülatörü Botu)
* `Assets/_Project/Scripts/Gameplay/Grid/` -> `SandSort.Gameplay.Grid` (Sahne üzerindeki görsel kum matrisi, `Simulation` katmanını görselleştiren `GridManager`)
* `Assets/_Project/Scripts/Gameplay/Containers/` -> `SandSort.Gameplay.Containers` (Alt kova kuyrukları, Aktif Toplama Slotu/Dock görselleri, DOTween geçişleri)
* `Assets/_Project/Scripts/Gameplay/Transfer/` -> `SandSort.Gameplay.Transfer` (Staggered akış kuyruğu, Bezier eğrisi ile kum çekme animasyonu, Havuz yönetimi)
* `Assets/_Project/Scripts/UI/` -> `SandSort.UI` (Kova yüzdeleri, Level seçici, hamle/slot uyarıları, Win/Fail ekranları)

---

## 3. Modül Detayları ve Algoritmik Tasarım

### Modül 1: Otomatik Seviye Üretici ve Çözülebilirlik Botu (Level EditorWindow)

Tasarımcının dışarıdan verdiği bir `.png` piksel sanat görselini okuyup hem üstteki kum matrisini hem de alttaki kova kuyruklarını otomatik inşa eden editör aracıdır.

* **Görsel Okuma ve Renk İndirgeme (Color Quantization):**
  * Kaynak `Texture2D` (Örn: $32 \times 32$ veya $48 \times 48$ ızgara boyutunda) taranır (Şeffaf pikseller $\alpha < 0.5$ boş hücre `-1` kabul edilir).
  * Her pikselin rengi, Öklid renk mesafesi formülü ile oyunun ana kum paletindeki ($P_1, P_2, \dots, P_m$) en yakın renge (`ColorID`) atanır:
    $$d = \sqrt{(R_2 - R_1)^2 + (G_2 - G_1)^2 + (B_2 - B_1)^2}$$
* **Hacim Hesabı, Kova Bölümleme ve Artık Dengeleme (Bucket Partitioning & Remainder Handling):**
  * Matristeki her `ColorID` için toplam kum/piksel sayısı ($N_{\text{color}}$) hesaplanır.
  * Bu toplam miktar, belirlenen standart kova kapasite aralıklarına (Örn: $C \in \{10, 15, 20, 25\}$) bölünür.
  * **Artık Kuralı (Edge-Case Koruması):** Eğer $N_{\text{color}}$ standart kapasitelere tam bölünmüyorsa, kalan kum miktarı ($R = N_{\text{color}} \pmod C$) ya son kovaya dinamik kapasite olarak atanır ya da son iki kova arasında dengeli paylaştırılarak toplam kova kapasitesinin matristeki kum sayısına **%100 eşit olması** garanti altına alınır:
    $$N_{\text{color}} = \sum_{k=1}^{B_{\text{color}}} C_k$$
* **Tersine Dağıtım (Reverse Scrambling) ile Kuyruk Üretimi:**
  * Kovalar alt sütunlara (Queue Columns) tamamen rastgele dizilirse bölüm ilk hamlede kilitlenebilir (Unwinnable).
  * Algoritma, resmin **alt sınırından (Exposed Frontier)** başlayarak yukarı doğru hangi renklerin sırayla açığa çıktığını simüle eder ve ürettiği kovaları bu açılma sırasına göre (belirli bir zorluk/karıştırma sapması $\sigma$ ekleyerek) alt sütunlara yerleştirir.
* **AI Çözülebilirlik Simülatörü (Headless Bot):**
  * Editör içindeki "Simulate $1000$ Runs" butonu, `SandSort.Simulation` çekirdeğini kullanarak görseli çizmeden arka planda bölümü $1000$ kez rastgele ve yarı-akıllı (greedy) stratejiyle oynar.
  * Kazanma oranı (Win Rate $\%$) raporlanır. Örneğin kazanma oranı $\%0$ ise dizilim hatalıdır; $\%25 - \%65$ arası ise ideal zorluktadır.

### Modül 2: Kum Matrisi ve Hücresel Otomat Fiziği (Grid & Gravity Engine)

Üstteki resmin tutulduğu, kumlar çekildikçe üstteki tanelerin aşağı aktığı ve yeni renklerin açığa çıktığı katmandır.

* **Matris Veri Yapısı:**
  * Boyutu $W \times H$ olan tek boyutlu `int[] GridCells` dizisi ($index = y \times W + x$).
  * `-1`: Boş hücre, `0..M`: İlgili `ColorID` değerine sahip kum/boncuk tanesi.
  * Genişletilebilirlik için hücre durumu (`CellState`: `Normal`, `Locked/Ice`, `Hidden`) desteklenir.
* **Açık Yüzey (Exposed Frontier) Kuralı:**
  * Bir kum tanesinin kova tarafından çekilebilir (vakumlanabilir) olması için alt sınırda veya açık yüzeyde bulunması gerekir ($y = 0$ satırında olması veya altındaki hücrenin `-1` olması).
* **Deterministik Hücresel Otomat Yerçekimi (Cellular Automata Gravity):**
  * Alt satırdan bir kum tanesi çekildiğinde hücre `-1` (boş) olur.
  * Her simülasyon adımında yukarıdan aşağıya yerçekimi kuralları işletilir:
    1. **Doğrudan Düşüş (`VerticalColumn`):** Eğer $(x, y)$ dolu ve $(x, y-1)$ boş ise, kum $(x, y-1)$ konumuna düşer.
    2. **Çapraz Kayma (`CellularAutomata`):** Eğer $(x, y-1)$ doluysa ancak $(x-1, y-1)$ veya $(x+1, y-1)$ boş ise kum çapraz aşağı kayar. Yön seçimi deterministik parite kuralıyla (`(x + y + stepTick) % 2 == 0`) yapılır.

### Modül 3: Kova Kuyruğu, Aktif Dock ve Deadlock Yönetimi (Containers)

Oyuncunun strateji kurduğu alt kova sütunları ve ortadaki aktif toplama alanıdır.

* **Alt Kova Sütunları (Spawner Queues):**
  * $S$ adet dikey sütundan (Örn: $4$ sütun) oluşur.
  * Oyuncu yalnızca sütunun en önünde/üstünde bulunan (önü açık) kovaya tıklayabilir. Tıklanan kova, ortadaki **Aktif Toplama Slotuna (Active Dock)** geçer ve arkadaki kovalar DOTween ile bir birim ileri kayar.
* **Aktif Toplama Slotu (Active Dock):**
  * Sabit kapasiteli bir dizidir (Örn: $K = 4$ veya $5$ slot).
  * Dock'a yerleşen bir kova, `GridManager` içindeki **açık yüzeyde (Exposed Frontier)** kendi `ColorID` değerine sahip kum tanesi varsa anında rezerve etmeye başlar.
* **Rezervasyon ve Kapasite Matematiği:**
  * Kovanın hedef kapasitesi $C_{\text{max}}$, o an içinde bulunan kum sayısı $C_{\text{current}}$ ve havada/kuyrukta o kovaya doğru gitmek üzere rezerve edilmiş kum sayısı $C_{\text{inFlight}}$ olarak tutulur.
  * Bir kova yalnızca şu koşul sağlandığında matristen yeni kum talep edebilir:
    $$C_{\text{current}} + C_{\text{inFlight}} < C_{\text{max}}$$
  * Kum matristen rezerve edildiği an $C_{\text{inFlight}}$ artırılır ve hücre matriste boşaltılır; kum görsel olarak kovaya ulaştığı an $C_{\text{inFlight}}$ azaltılıp $C_{\text{current}}$ artırılır. Doluluk oranı yüzdesi:
    $$\text{Progress (\%)} = \left\lfloor \frac{C_{\text{current}}}{C_{\text{max}}} \times 100 \right\rfloor$$
  * $C_{\text{current}} == C_{\text{max}}$ ($100\%$) olduğunda kova tamamlanma animasyonuyla (Scale Punch + çıkış) slottan ayrılır ve slot boşalır.
* **Kilitlenme (Deadlock / Fail State) Algoritması:**
  * Eğer Aktif Dock'taki tüm slotlar doluysa ($\text{OccupiedSlots} == K$), hiçbir kovaya doğru uçan veya çıkış kuyruğunda bekleyen kum yoksa ($\sum C_{\text{inFlight}} == 0$), yerçekimi hareketi tamamen durmuşsa (`IsGridSettled == true`) ve Dock'taki kovaların renklerinden **hiçbiri** matrisin açık yüzeyindeki (`Exposed Frontier`) renklerle eşleşmiyorsa **Deadlock** tetiklenir ve bölüm kaybedilir.

### Modül 4: Görsel Kum Akışı, Staggered Juice ve Havuzlama (Transfer & VFX)

Binlerce kum tanesinin matristeki konumundan çıkıp ilgili kovaya doğru akıcı bir eğriyle uçmasını sağlayan performans katmanıdır.

* **Staggered Emission (Sıralı Vakumlama Hissi):**
  * Tek bir karede 20 kum rezerve edilse dahi, tüm tanelerin aynı anda üst üste uçmasını önlemek için her kova kendi çıkış zamanlayıcısına (`NextEmitTime`) sahiptir. Taneler $0.02\text{ sn} - 0.03\text{ sn}$ aralıklarla peş peşe bir hortum/vakum akışı gibi fırlatılır.
* **Kuadratik/Kübik Bezier Eğrisi ile Hareket:**
  * Matristeki başlangıç noktası $P_0$, kovanın giriş ağzı $P_2$ ve aradaki dinamik kontrol noktası $P_1$ olmak üzere, uçan her kum tanesinin $t \in [0, 1]$ anındaki konumu:
    $$B(t) = (1 - t)^2 P_0 + 2(1 - t)t P_1 + t^2 P_2$$
* **Bellek ve Render Optimizasyonu (WebGL & Mobile Ready):**
  * Sahnedeki uçan kum/boncuk taneleri için sabit boyutlu bir `ObjectPool` (Örn: $256$ elemanlı) ve ortak `MaterialPropertyBlock` kullanılır.
  * Kova dolarken kovanın içindeki kum yüksekliği `MaterialPropertyBlock` veya iç dolgu mesh ölçeklendirmesi ile $0.0 \to 1.0$ aralığında pürüzsüz yükseltilir.

---

## 4. Geliştirme Fazları ve Takip Çizelgesi (Checklist)

### Faz 1: Çekirdek Mimari, Veri Yapıları ve Pure C# Simülasyon Çekirdeği
* [x] Klasör hiyerarşisi ve `.asmdef` dosyalarının oluşturulması (`SandSort.Core`, `SandSort.Data`, `SandSort.Simulation`, `SandSort.Gameplay`, `SandSort.UI`, `SandSort.EditorTools`)
* [x] `EventBus` (struct tabanlı, Zero-GC) ve pre-allocated `ObjectPool<T>` sınıflarının yazılması
* [x] `LevelDataSO`, `BucketData`, `GravityMode` veri modellerinin kurulması
* [x] Bot ve Runtime'ın ortak kullanacağı Pure C# `BoardSimulator` sınıfının (Grid, Gravity, Exposed Frontier, Dock Reservation) yazılması

### Faz 2: Otomatik Seviye Üretici ve Çözülebilirlik Botu (Editor Tool)
* [x] PNG görselinden matris ve kova kuyruğu üreten `LevelGeneratorWindow` aracının yazılması (Quantization + Remainder Handling + Reverse Scrambling)
* [x] Headless Bot simülatörü ile $1000$ koşuluk çözülebilirlik (Win Rate $\%$) testinin doğrulanması
* [x] WebGL vitrini için 2-3 adet örnek piksel sanat seviyesinin (`Level_01`, `Level_02`, `Level_03`) üretilmesi

### Faz 3: Sahne Kum Matrisi ve Hücresel Otomat Görselleştirmesi (Grid Engine)
* [x] `GridManager` ile $W \times H$ matrisin sahnede Zero-GC prensibiyle oluşturulması ve görselleştirilmesi
* [x] Açık yüzey (Exposed Frontier) vurgusu ve yerçekimi düşüşünün akıcı interpolasyonu

### Faz 4: Kova Kuyruğu, Aktif Dock ve Deadlock Sistemi (Containers)
* [x] Alt sütunlardan tıklanan kovanın Aktif Dock'a geçişi ve DOTween kuyruk kaydırma animasyonu
* [x] Rezervasyon matematiği ($C_{\text{current}} + C_{\text{inFlight}} < C_{\text{max}}$) ile hatasız kum çekimi
* [x] Deadlock (Kilitlenme) ve Bölüm Tamamlama (Win) durumlarının doğru tetiklenmesi

### Faz 5: Bezier Kum Akışı ve Görsel Havuzlama (Transfer & VFX)
* [x] Staggered Emission kuyruğu ve Bezier eğrisi ile kum tanelerinin matristen kovalara akıcı uçuşu
* [x] Kova doluluk yüzdesi ($0\% \to 100\%$) ve `MaterialPropertyBlock` ile doluluk görselleştirmesi

### Faz 6: Arayüz, Tek Tıkla Sahne Kurulumu ve WebGL Portfolyo Hazırlığı (UI & Polish)
* [x] `UIManager` (Level geçişleri, ilerleme çubuğu, Win/Fail panelleri)
* [x] `SandSort/Setup Playable Test Scene (Auto-Wire Prefabs)` Editor menü komutu ile sahnenin tek tıkla otomatik inşa edilmesi
* [ ] WebGL Build ve Profiler Zero-GC kontrolü

