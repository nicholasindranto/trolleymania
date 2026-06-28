using UnityEngine;

/// <summary>
/// Script ini berfungsi sebagai detektor garis finish (Cashier Goal) ketika player selesai berbelanja.
/// Jika semua objective belanja telah terpenuhi dan trolley menyentuh trigger ini, game akan di-pause dan UI kemenangan ditampilkan.
/// </summary>
public class CashierGoal : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("Panel UI kemenangan (BGYouWin) yang akan diaktifkan saat pemain menang.")]
    [SerializeField] private GameObject bgYouWinPanel;

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Memastikan panel You Win dinonaktifkan di awal permainan agar tidak menghalangi HUD.
        if (bgYouWinPanel != null)
        {
            bgYouWinPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("CashierGoal: bgYouWinPanel belum di-assign di Inspector!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // LOGIC DI BALIK LAYAR (Optimasi WebGL):
        // Memeriksa tag terlebih dahulu untuk menghindari panggilan GetComponent yang mahal pada objek non-player/non-trolley.
        if (other.CompareTag("PlayerTrolley") || other.CompareTag("Player"))
        {
            TrolleyController trolley = other.GetComponent<TrolleyController>();
            if (trolley == null) trolley = other.GetComponentInParent<TrolleyController>();
            if (trolley == null) trolley = other.GetComponentInChildren<TrolleyController>();

            // Jika objek yang masuk berhubungan dengan Trolley
            if (trolley != null)
            {
                // Periksa apakah ObjectiveManager terdaftar dan semua objektif belanja telah selesai
                if (ObjectiveManager.Instance != null)
                {
                    if (ObjectiveManager.Instance.AreAllObjectivesCompleted())
                    {
                        WinGame();
                    }
                    else
                    {
                        Debug.Log("CashierGoal: Pemain menyentuh kasir, tetapi masih ada belanjaan yang belum lengkap!");
                    }
                }
                else
                {
                    Debug.LogError("CashierGoal: ObjectiveManager tidak ditemukan di scene!");
                }
            }
        }
    }

    /// <summary>
    /// Logika untuk memenangkan permainan, mem-pause game, dan memunculkan UI Win.
    /// </summary>
    private void WinGame()
    {
        Debug.Log("CashierGoal: Selamat! Semua objektif terpenuhi. Anda Menang!");

        // 1. Tampilkan panel UI kemenangan (BGYouWin)
        if (bgYouWinPanel != null)
        {
            bgYouWinPanel.SetActive(true);

            // 2. LOGIC DI BALIK LAYAR: Hitung dan tampilkan score akhir menggunakan ScoreManager
            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.EndGame(bgYouWinPanel);
            }
        }

        // 3. LOGIC DI BALIK LAYAR (Time.timeScale = 0):
        //    Menyetel Time.timeScale ke 0 akan menghentikan seluruh pembaruan waktu fisika (FixedUpdate) 
        //    dan update berbasis waktu (Time.deltaTime) di Unity. Ini secara efektif mem-pause gameplay 
        //    sehingga player tidak bisa menggerakkan trolley lagi setelah menang.
        Time.timeScale = 0f;
    }
}
