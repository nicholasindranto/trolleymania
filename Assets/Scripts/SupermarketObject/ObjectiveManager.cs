using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Class untuk menyimpan status barang belanjaan yang harus diambil dalam list belanja.
/// </summary>
[System.Serializable]
public class ObjectiveItem
{
    public string itemName;      // Nama barang belanjaan (sesuai ObjName pada ObjectScript)
    public int requiredAmount;   // Jumlah target yang harus diambil
    public int currentAmount;    // Jumlah saat ini yang sudah masuk trolley
}

/// <summary>
/// Manager pusat untuk memproses spawn barang secara asynchronous (perlahan) di awal permainan,
/// mengacak tugas belanja (Objective), dan memperbarui seluruh UI terkait belanjaan.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("Spawning Settings")]
    [Tooltip("Daftar prefab objek (Goods / Weapon) yang siap di-spawn ke rak-rak supermarket.")]
    [SerializeField] private GameObject[] prefabsToSpawn;

    [Tooltip("Parent transform yang berisi semua titik spawn (spawn points).")]
    [SerializeField] private Transform spawnPointsRoot;

    [Tooltip("Parent transform untuk mengelompokkan semua objek belanjaan yang di-spawn (mencegah Find global).")]
    [SerializeField] private Transform spawnedObjectsParent;

    [Tooltip("Parent transform khusus untuk objek belanjaan (Collectable) yang di-spawn.")]
    [SerializeField] private Transform collectableObjectsParent;

    [Tooltip("Parent transform khusus untuk objek senjata/lemparan (Throwable) yang di-spawn.")]
    [SerializeField] private Transform throwableObjectsParent;

    [Tooltip("Waktu jeda (detik) antar setiap spawn objek untuk meminimalkan beban CPU (terutama di mobile).")]
    [SerializeField] private float spawnDelay = 0.05f;

    [Header("Highlight/Outline Settings")]
    [Tooltip("Warna highlight garis tepi (outline) untuk barang belanjaan.")]
    [SerializeField] private Color highlightColor = Color.yellow;

    [Tooltip("Ketebalan highlight garis tepi (outline) untuk barang belanjaan.")]
    [SerializeField] private float highlightWidth = 4f;

    [Header("UI Preview References")]
    [Tooltip("GameObject Text untuk nama Goods 1.")]
    [SerializeField] private GameObject goods1NameText;
    [Tooltip("GameObject Text untuk progres/jumlah Goods 1.")]
    [SerializeField] private GameObject goods1ProgressText;

    [Tooltip("GameObject Text untuk nama Goods 2.")]
    [SerializeField] private GameObject goods2NameText;
    [Tooltip("GameObject Text untuk progres/jumlah Goods 2.")]
    [SerializeField] private GameObject goods2ProgressText;

    [Header("UI List References")]
    [Tooltip("Panel latar belakang dari seluruh shopping list (BGListObjective).")]
    [SerializeField] private GameObject bgListObjective;

    [Tooltip("GameObject Text di dalam panel list untuk mencetak seluruh daftar barang belanjaan.")]
    [SerializeField] private GameObject listText;

    // Cache komponen teks untuk menghindari GetComponent runtime
    private TMPro.TextMeshProUGUI goods1NameTMP;
    private TMPro.TextMeshProUGUI goods1ProgressTMP;
    private TMPro.TextMeshProUGUI goods2NameTMP;
    private TMPro.TextMeshProUGUI goods2ProgressTMP;
    private TMPro.TextMeshProUGUI listTMP;

    private UnityEngine.UI.Text goods1NameLegacy;
    private UnityEngine.UI.Text goods1ProgressLegacy;
    private UnityEngine.UI.Text goods2NameLegacy;
    private UnityEngine.UI.Text goods2ProgressLegacy;
    private UnityEngine.UI.Text listLegacy;

    // List internal yang menampung daftar belanja acak pemain
    private List<ObjectiveItem> objectives = new List<ObjectiveItem>();

    private void Awake()
    {
        // PENTING: Setel ulang timeScale ke 1.0f setiap kali scene di-load. 
        // Ini mencegah game tetap ter-pause ketika scene di-restart dari menu 'PlayAgain'.
        Time.timeScale = 1f;

        // LOGIC DI BALIK LAYAR (Singleton Pattern):
        // Memastikan hanya ada satu Instance ObjectiveManager di dalam permainan agar mudah diakses oleh script lain.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Cache komponen teks di awal game
        CacheTextComponents();

        // LOGIC DI BALIK LAYAR:
        // Sesuai alur yang baru, kita acak terlebih dahulu rancangan list belanjaan (objective) pemain.
        // Berdasarkan list tersebut, kita tahu barang apa saja yang HARUS disiapkan di rak supermarket
        // agar permainan dijamin dapat diselesaikan (solvable).
        GenerateRandomObjectives();
    }

    /// <summary>
    /// Meng-cache komponen teks UI secara dini agar performa WebGL tetap optimal.
    /// </summary>
    private void CacheTextComponents()
    {
        if (goods1NameText != null)
        {
            goods1NameTMP = goods1NameText.GetComponent<TMPro.TextMeshProUGUI>();
            if (goods1NameTMP == null) goods1NameLegacy = goods1NameText.GetComponent<UnityEngine.UI.Text>();
        }
        if (goods1ProgressText != null)
        {
            goods1ProgressTMP = goods1ProgressText.GetComponent<TMPro.TextMeshProUGUI>();
            if (goods1ProgressTMP == null) goods1ProgressLegacy = goods1ProgressText.GetComponent<UnityEngine.UI.Text>();
        }
        if (goods2NameText != null)
        {
            goods2NameTMP = goods2NameText.GetComponent<TMPro.TextMeshProUGUI>();
            if (goods2NameTMP == null) goods2NameLegacy = goods2NameText.GetComponent<UnityEngine.UI.Text>();
        }
        if (goods2ProgressText != null)
        {
            goods2ProgressTMP = goods2ProgressText.GetComponent<TMPro.TextMeshProUGUI>();
            if (goods2ProgressTMP == null) goods2ProgressLegacy = goods2ProgressText.GetComponent<UnityEngine.UI.Text>();
        }
        if (listText != null)
        {
            listTMP = listText.GetComponent<TMPro.TextMeshProUGUI>();
            if (listTMP == null) listLegacy = listText.GetComponent<UnityEngine.UI.Text>();
        }
    }

    /// <summary>
    /// Membuat misi belanja acak dengan total target barang antara 5 sampai 8 unit dari prefabsToSpawn.
    /// </summary>
    private void GenerateRandomObjectives()
    {
        // 1. Kumpulkan semua prefab unik dari prefabsToSpawn sebelum barang di-spawn ke scene
        List<GameObject> availableGoodsPrefabs = new List<GameObject>();
        List<string> uniqueGoodsNames = new List<string>();

        foreach (GameObject prefab in prefabsToSpawn)
        {
            if (prefab == null) continue;

            ObjectScript objScript = prefab.GetComponent<ObjectScript>();
            if (objScript == null) objScript = prefab.GetComponentInParent<ObjectScript>();
            if (objScript == null) objScript = prefab.GetComponentInChildren<ObjectScript>();

            if (objScript != null)
            {
                if (!uniqueGoodsNames.Contains(objScript.ObjName))
                {
                    uniqueGoodsNames.Add(objScript.ObjName);
                    availableGoodsPrefabs.Add(prefab);
                }
            }
        }

        if (availableGoodsPrefabs.Count == 0)
        {
            Debug.LogError("ObjectiveManager: Tidak ditemukan prefab di array prefabsToSpawn!");
            return;
        }

        // 2. Acak urutan daftar prefab Goods yang tersedia
        for (int i = 0; i < availableGoodsPrefabs.Count; i++)
        {
            GameObject tempPrefab = availableGoodsPrefabs[i];
            int randomIndex = Random.Range(i, availableGoodsPrefabs.Count);
            availableGoodsPrefabs[i] = availableGoodsPrefabs[randomIndex];
            availableGoodsPrefabs[randomIndex] = tempPrefab;
        }

        // 3. Tentukan jumlah total target belanja acak (5 sampai 8)
        int targetTotal = Random.Range(5, 9);
        int remainingAmount = targetTotal;

        // Ambil maksimal 3 jenis barang agar daftar belanja tidak terlalu rumit
        int numTypes = Mathf.Min(3, availableGoodsPrefabs.Count);
        
        // Bersihkan list objective lama
        objectives.Clear();

        // List pembantu untuk menampung prefab barang belanjaan yang wajib di-spawn
        List<GameObject> requiredSpawns = new List<GameObject>();

        // 4. Distribusikan jumlah target secara acak ke barang yang terpilih
        for (int i = 0; i < numTypes; i++)
        {
            GameObject chosenPrefab = availableGoodsPrefabs[i];
            ObjectScript objScript = chosenPrefab.GetComponent<ObjectScript>();
            if (objScript == null) objScript = chosenPrefab.GetComponentInParent<ObjectScript>();
            if (objScript == null) objScript = chosenPrefab.GetComponentInChildren<ObjectScript>();

            ObjectiveItem item = new ObjectiveItem();
            item.itemName = objScript.ObjName;

            if (i == numTypes - 1)
            {
                // Item terakhir menampung seluruh sisa target
                item.requiredAmount = remainingAmount;
            }
            else
            {
                // Berikan jumlah minimal 1, dan maksimal disisakan 1 untuk setiap slot berikutnya
                int maxPossible = remainingAmount - (numTypes - 1 - i);
                item.requiredAmount = Random.Range(1, maxPossible + 1);
            }

            item.currentAmount = 0;
            remainingAmount -= item.requiredAmount;
            objectives.Add(item);

            // Masukkan prefab barang belanjaan ini sebanyak target belanja ke list wajib spawn
            for (int k = 0; k < item.requiredAmount; k++)
            {
                requiredSpawns.Add(chosenPrefab);
            }
        }

        // 5. Mulai proses spawn bertahap dengan mengirimkan daftar barang belanjaan wajib
        StartCoroutine(SpawnObjectsSlowly(requiredSpawns));
    }

    /// <summary>
    /// Coroutine untuk men-spawn objek satu per satu secara perlahan.
    /// Menjamin semua barang belanjaan wajib di-spawn terlebih dahulu, baru mengisi sisa titik spawn dengan barang acak.
    /// </summary>
    private IEnumerator SpawnObjectsSlowly(List<GameObject> requiredSpawns)
    {
        if (bgListObjective != null)
        {
            bgListObjective.SetActive(false);
        }

        if (spawnPointsRoot == null || prefabsToSpawn == null || prefabsToSpawn.Length == 0)
        {
            Debug.LogWarning("ObjectiveManager: SpawnPoints atau PrefabsToSpawn kosong!");
            yield break;
        }

        int totalSpawnPoints = spawnPointsRoot.childCount;

        // 1. Buat daftar antrean spawn kosong
        List<GameObject> spawnQueue = new List<GameObject>();

        // 2. Masukkan seluruh barang belanjaan wajib (objective) ke dalam antrean spawn
        //    PENTING: Kita batasi agar jumlah barang wajib tidak melebihi kapasitas titik spawn di map
        int requiredCount = Mathf.Min(requiredSpawns.Count, totalSpawnPoints);
        for (int i = 0; i < requiredCount; i++)
        {
            spawnQueue.Add(requiredSpawns[i]);
        }

        // 3. Isi sisa titik spawn kosong di supermarket dengan prefab acak dari daftar prefabsToSpawn
        int remainingSlots = totalSpawnPoints - spawnQueue.Count;
        for (int i = 0; i < remainingSlots; i++)
        {
            GameObject randomPrefab = prefabsToSpawn[Random.Range(0, prefabsToSpawn.Length)];
            spawnQueue.Add(randomPrefab);
        }

        // 4. LOGIC DI BALIK LAYAR (Pencegahan Pola Spawn Berkumpul / Shuffling):
        //    Jika kita langsung melakukan instansiasi, barang-barang wajib belanjaan akan menumpuk di area awal (spawn points index awal).
        //    Solusi: Kita lakukan pengacakan urutan (Shuffle) pada antrean menggunakan Fisher-Yates shuffle algorithm.
        //    Ini menjamin persebaran barang belanjaan menyebar rata secara random di seluruh supermarket.
        for (int i = 0; i < spawnQueue.Count; i++)
        {
            GameObject temp = spawnQueue[i];
            int randomIndex = Random.Range(i, spawnQueue.Count);
            spawnQueue[i] = spawnQueue[randomIndex];
            spawnQueue[randomIndex] = temp;
        }

        // 5. Lakukan spawn secara perlahan satu per satu menggunakan jeda frame
        for (int i = 0; i < totalSpawnPoints; i++)
        {
            Transform spawnPoint = spawnPointsRoot.GetChild(i);
            GameObject prefab = spawnQueue[i];
            
            if (prefab != null)
            {
                // Spawn objek baru
                GameObject spawnedObj = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                
                // LOGIC DI BALIK LAYAR (Optimalisasi Mobile - Group Parenting):
                // Mengelompokkan semua barang belanjaan hasil spawn ke dalam parent khusus.
                if (collectableObjectsParent != null)
                {
                    spawnedObj.transform.SetParent(collectableObjectsParent);
                }
                else if (spawnedObjectsParent != null)
                {
                    spawnedObj.transform.SetParent(spawnedObjectsParent);
                }
            }

            // Berikan jeda frame agar CPU dapat bernapas
            yield return new WaitForSeconds(spawnDelay);
        }

        // 6. Perbarui tampilan UI belanja untuk pertama kalinya setelah seluruh barang ter-spawn
        UpdateAllUI();

        // Perbarui sorotan visual (highlight) awal pada barang-barang belanjaan
        UpdateObjectHighlights();
    }

    /// <summary>
    /// Dipanggil dari TrolleyAreaDetector ketika ada barang belanjaan yang masuk (+1) atau keluar (-1) dari trolley.
    /// </summary>
    public void UpdateObjectiveProgress(string itemName, int amountChange)
    {
        // Cari barang yang sesuai di dalam list objective
        ObjectiveItem targetItem = objectives.Find(x => x.itemName == itemName);
        
        if (targetItem != null)
        {
            // Perbarui jumlah saat ini (dibatasi tidak boleh kurang dari 0 atau melebihi batas maksimal)
            targetItem.currentAmount = Mathf.Clamp(targetItem.currentAmount + amountChange, 0, targetItem.requiredAmount + 100);
            
            // Perbarui tampilan UI secara menyeluruh
            UpdateAllUI();

            // Perbarui sorotan visual (highlight) barang karena progres belanjanya telah berubah
            UpdateObjectHighlights();
        }
    }

    /// <summary>
    /// Memeriksa apakah seluruh misi belanja belanjaan telah berhasil diselesaikan (currentAmount >= requiredAmount).
    /// </summary>
    public bool AreAllObjectivesCompleted()
    {
        // Jika list belanja kosong, berarti tidak ada misi
        if (objectives == null || objectives.Count == 0) return false;

        foreach (var item in objectives)
        {
            // Jika ada satu saja barang yang jumlahnya di bawah target, maka belum selesai
            if (item.currentAmount < item.requiredAmount)
            {
                return false;
            }
        }

        // Semua target barang belanjaan telah terpenuhi!
        return true;
    }

    /// <summary>
    /// Menyalakan / mematikan panel BGListObjective (Toggle) dan memperbarui sorotan visual barang di scene.
    /// </summary>
    public void ToggleBGListObjective()
    {
        if (bgListObjective != null)
        {
            bgListObjective.SetActive(!bgListObjective.activeSelf);
            
            // PENTING: Setiap kali list dibuka, pastikan UI list diperbarui
            if (bgListObjective.activeSelf)
            {
                UpdateFullListUI();
            }

            // Perbarui sorotan visual (highlight) mengikuti aktifnya panel list belanjaan
            UpdateObjectHighlights();
        }
    }

    /// <summary>
    /// Memperbarui status outline/highlight untuk semua objek belanjaan yang telah di-spawn.
    /// Hanya objek yang termasuk dalam target belanja yang belum terpenuhi yang akan menyala.
    /// Mengurangi beban kerja di mobile dengan membatasi iterasi hanya pada anak-anak spawnedObjectsParent.
    /// </summary>
    private void UpdateObjectHighlights()
    {
        // Mode highlight aktif jika panel list belanja sedang terbuka
        bool isHighlightActive = bgListObjective != null && bgListObjective.activeSelf;

        // Kumpulkan daftar nama barang belanjaan yang masih kurang (belum terpenuhi)
        HashSet<string> incompleteItemNames = new HashSet<string>();
        foreach (var item in objectives)
        {
            if (item.currentAmount < item.requiredAmount)
            {
                incompleteItemNames.Add(item.itemName);
            }
        }

        // Tentukan target parent untuk iterasi. Prioritas utama adalah collectableObjectsParent, fallback ke spawnedObjectsParent
        Transform searchParent = collectableObjectsParent != null ? collectableObjectsParent : spawnedObjectsParent;
        if (searchParent == null) return;

        // Loop ke semua anak objek di bawah parent transform khusus belanjaan
        foreach (Transform child in searchParent)
        {
            if (child == null) continue;

            // Cari komponen ObjectScript untuk mencocokkan nama barang
            ObjectScript objScript = child.GetComponent<ObjectScript>();
            if (objScript == null) objScript = child.GetComponentInParent<ObjectScript>();
            if (objScript == null) objScript = child.GetComponentInChildren<ObjectScript>();

            if (objScript != null)
            {
                // Tentukan apakah objek ini harus menyala (active + masuk daftar target belanja yang belum selesai)
                bool shouldHighlight = isHighlightActive && incompleteItemNames.Contains(objScript.ObjName);

                // Dapatkan atau buat komponen Outline secara dinamis jika belum terpasang
                Outline outline = child.GetComponent<Outline>();
                if (outline == null) outline = child.GetComponentInChildren<Outline>();

                if (outline == null)
                {
                    outline = child.gameObject.AddComponent<Outline>();
                    
                    // Gunakan konfigurasi warna dan lebar dari Inspector
                    outline.OutlineMode = Outline.Mode.OutlineAll;
                    outline.OutlineColor = highlightColor;
                    outline.OutlineWidth = highlightWidth;
                }

                // Nyalakan atau matikan outline sesuai status
                outline.ToggleOutline(shouldHighlight);
            }
        }
    }

    /// <summary>
    /// Memperbarui seluruh visual UI (Quick Preview dan Full List).
    /// </summary>
    private void UpdateAllUI()
    {
        UpdateQuickPreviewUI();
        UpdateFullListUI();
    }

    /// <summary>
    /// Memperbarui tampilan 2 barang di Quick Preview (goods1 & goods2).
    /// Menampilkan barang yang BELUM selesai dikumpulkan, diurutkan berdasarkan persentase kemajuan terkecil.
    /// </summary>
    private void UpdateQuickPreviewUI()
    {
        // 1. Pisahkan barang yang belum selesai dengan barang yang sudah selesai
        List<ObjectiveItem> incomplete = new List<ObjectiveItem>();
        List<ObjectiveItem> completed = new List<ObjectiveItem>();

        foreach (var item in objectives)
        {
            if (item.currentAmount < item.requiredAmount)
            {
                incomplete.Add(item);
            }
            else
            {
                completed.Add(item);
            }
        }

        // 2. Urutkan yang belum selesai berdasarkan persentase kemajuan terendah (agar prioritas barang tersulit ditampilkan dulu)
        incomplete.Sort((a, b) => ((float)a.currentAmount / a.requiredAmount).CompareTo((float)b.currentAmount / b.requiredAmount));

        // 3. Satukan kembali daftar display (yang belum selesai tampil paling depan)
        List<ObjectiveItem> sortedDisplay = new List<ObjectiveItem>();
        sortedDisplay.AddRange(incomplete);
        sortedDisplay.AddRange(completed);

        // 4. Update Slot 1
        if (sortedDisplay.Count >= 1)
        {
            SetText(goods1NameText, sortedDisplay[0].itemName);
            SetText(goods1ProgressText, $"({sortedDisplay[0].currentAmount}/{sortedDisplay[0].requiredAmount})");
        }
        else
        {
            SetText(goods1NameText, "-");
            SetText(goods1ProgressText, "(0/0)");
        }

        // 5. Update Slot 2
        if (sortedDisplay.Count >= 2)
        {
            SetText(goods2NameText, sortedDisplay[1].itemName);
            SetText(goods2ProgressText, $"({sortedDisplay[1].currentAmount}/{sortedDisplay[1].requiredAmount})");
        }
        else
        {
            SetText(goods2NameText, "-");
            SetText(goods2ProgressText, "(0/0)");
        }
    }

    /// <summary>
    /// Memperbarui tampilan list lengkap belanjaan di BGListObjective.
    /// </summary>
    private void UpdateFullListUI()
    {
        if (listText == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < objectives.Count; i++)
        {
            var item = objectives[i];
            bool isCompleted = item.currentAmount >= item.requiredAmount;
            // Gunakan warna hijau jika selesai (menggunakan 'V' karena karakter unicode centang tidak ter-render oleh font TMPro default), dan orange jika belum
            string prefix = isCompleted ? "<color=green>[V]</color>" : "<color=orange>[-]</color>";
            sb.Append(prefix).Append(" ").Append(item.itemName).Append(" (").Append(item.currentAmount).Append("/").Append(item.requiredAmount).Append(")\n");
        }

        SetTextCached(listText, sb.ToString(), listTMP, listLegacy);
    }

    /// <summary>
    /// Fungsi pembantu (helper) untuk memperbarui string text pada GameObject UI,
    /// secara dinamis mendeteksi TextMeshProUGUI maupun UI Text standar Unity.
    /// </summary>
    private void SetText(GameObject go, string content)
    {
        if (go == null) return;

        if (go == goods1NameText) SetTextCached(go, content, goods1NameTMP, goods1NameLegacy);
        else if (go == goods1ProgressText) SetTextCached(go, content, goods1ProgressTMP, goods1ProgressLegacy);
        else if (go == goods2NameText) SetTextCached(go, content, goods2NameTMP, goods2NameLegacy);
        else if (go == goods2ProgressText) SetTextCached(go, content, goods2ProgressTMP, goods2ProgressLegacy);
        else if (go == listText) SetTextCached(go, content, listTMP, listLegacy);
        else
        {
            // Fallback jika memanggil game object non-default
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = content;
                return;
            }
            var txt = go.GetComponent<UnityEngine.UI.Text>();
            if (txt != null)
            {
                txt.text = content;
            }
        }
    }

    /// <summary>
    /// Mengeset teks secara instan menggunakan referensi ter-cache (hemat CPU).
    /// </summary>
    private void SetTextCached(GameObject go, string content, TMPro.TextMeshProUGUI tmp, UnityEngine.UI.Text txt)
    {
        if (tmp != null)
        {
            tmp.text = content;
        }
        else if (txt != null)
        {
            txt.text = content;
        }
    }
}
