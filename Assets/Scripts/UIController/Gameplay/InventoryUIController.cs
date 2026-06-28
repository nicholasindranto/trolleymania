using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mengontrol tampilan UI list Inventory (isi barang di dalam trolley, PUBG Mobile style).
/// Menggunakan sistem Object Pool untuk performa WebGL Mobile yang optimal (zero GC allocation).
/// </summary>
public class InventoryUIController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke TrolleyAreaDetector untuk mengambil daftar barang di dalam trolley.")]
    [SerializeField] private TrolleyAreaDetector trolleyAreaDetector;

    [Tooltip("Referensi ke HUDController untuk melengkapi (equip) barang ke tangan.")]
    [SerializeField] private HUDController hudController;

    [Header("UI Layout References")]
    [Tooltip("Panel penampung utama UI Inventory (biasanya memiliki ScrollRect).")]
    [SerializeField] private GameObject mainPanel;

    [Tooltip("Transform Content tempat tombol-tombol item di-spawn (Vertical Layout Group).")]
    [SerializeField] private Transform contentContainer;

    [Tooltip("GameObject NotifText untuk menampilkan pesan peringatan ketika list item trolley kosong.")]
    [SerializeField] private GameObject emptyNotifText;

    [Header("Prefabs & Assets")]
    [Tooltip("Prefab tombol item di trolley (TrolleyItemButton).")]
    [SerializeField] private TrolleyItemButton trolleyItemButtonPrefab;

    [Tooltip("Ikon default jika barang belum dikonfigurasikan di ScriptableObject.")]
    [SerializeField] private Sprite defaultItemIcon;

    // Pool tombol UI untuk didaur ulang secara instan
    private readonly List<TrolleyItemButton> buttonPool = new List<TrolleyItemButton>();

    private void Start()
    {
        // Auto-assign referensi jika dikosongkan di Inspector
        if (trolleyAreaDetector == null)
        {
            trolleyAreaDetector = FindObjectOfType<TrolleyAreaDetector>();
        }

        if (hudController == null)
        {
            hudController = FindObjectOfType<HUDController>();
        }

        // Daftarkan listener ke event-driven system agar UI ter-update otomatis ketika isi trolley berubah
        if (trolleyAreaDetector != null)
        {
            trolleyAreaDetector.OnTrolleyItemsChanged += RefreshUI;
        }

        // Tutup panel inventory di awal game
        if (mainPanel != null)
        {
            mainPanel.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe listener untuk menghindari memory leak di WebGL
        if (trolleyAreaDetector != null)
        {
            trolleyAreaDetector.OnTrolleyItemsChanged -= RefreshUI;
        }
    }

    /// <summary>
    /// Membuka atau menutup panel inventory (dipanggil saat tombol Backpack diklik).
    /// </summary>
    public void ToggleInventory()
    {
        if (mainPanel == null) return;

        bool nextState = !mainPanel.activeSelf;
        mainPanel.SetActive(nextState);

        if (nextState)
        {
            RefreshUI();
        }
    }

    /// <summary>
    /// Memperbarui rendering list UI berdasarkan data isi trolley terkini.
    /// </summary>
    public void RefreshUI()
    {
        if (trolleyAreaDetector == null || contentContainer == null || trolleyItemButtonPrefab == null)
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            return;
        }

        // Dapatkan data items terkini di dalam trolley
        List<ObjectScript> items = trolleyAreaDetector.ItemsInTrolley;
        
        // Bersihkan data null (antisipasi jika ada objek yang hancur karena alasan tertentu)
        items.RemoveAll(item => item == null);

        int itemCount = items.Count;

        // OPTIMALISASI MOBILE WEBGL & KODE SPESIALISASI:
        // Cukup ubah status keaktifan GameObject notifText secara instan (tanpa alokasi memori GC).
        // Menampilkan NotifText jika jumlah barang == 0, dan menyembunyikannya jika > 0.
        if (emptyNotifText != null)
        {
            emptyNotifText.SetActive(itemCount == 0);
        }

        // 1. Nonaktifkan semua tombol aktif di pool untuk daur ulang bersih
        for (int i = 0; i < buttonPool.Count; i++)
        {
            if (buttonPool[i] != null)
            {
                buttonPool[i].gameObject.SetActive(false);
            }
        }

        // 2. Render tombol dari item yang ada di dalam trolley
        for (int i = 0; i < itemCount; i++)
        {
            ObjectScript currentItem = items[i];
            if (currentItem == null) continue;

            TrolleyItemButton buttonInstance;

            // Gunakan pool jika tersedia
            if (i < buttonPool.Count)
            {
                buttonInstance = buttonPool[i];
            }
            else
            {
                buttonInstance = Instantiate(trolleyItemButtonPrefab, contentContainer);
                buttonPool.Add(buttonInstance);
            }

            if (buttonInstance != null)
            {
                buttonInstance.gameObject.SetActive(true);

                // Buat salinan variabel lokal untuk menghindari isu closure di WebGL C#
                ObjectScript selectedItem = currentItem;
                
                // Initialize tombol. Ketika diklik, item akan di-equip ke tangan player
                buttonInstance.Initialize(
                    selectedItem,
                    () => OnItemClicked(selectedItem),
                    defaultItemIcon
                );
            }
        }
    }

    /// <summary>
    /// Dipanggil ketika salah satu item dalam list inventory diklik.
    /// </summary>
    private void OnItemClicked(ObjectScript item)
    {
        if (hudController != null && item != null)
        {
            // Ambil item dari trolley dan tempatkan ke tangan player
            hudController.EquipItemFromInventory(item);
            
            // Tutup panel inventory setelah memilih item agar player bisa fokus melempar
            if (mainPanel != null)
            {
                mainPanel.SetActive(false);
            }

            // Perbarui UI inventory
            RefreshUI();
        }
    }
}
