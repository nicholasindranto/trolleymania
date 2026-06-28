using UnityEngine;
using TMPro;

/// <summary>
/// Pengelola skor dan reward pemain (ScoreManager).
/// Berfungsi mencatat waktu permainan, jumlah NPC ter-KO, serta mengalkulasi
/// skor akhir berdasarkan GDD (Time Bonus, NPC KO, Player HP, dan Trolley Durability).
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Score Configuration")]
    [Tooltip("Skor dasar untuk bonus waktu.")]
    [SerializeField] private int baseTimeScore = 2000;

    [Tooltip("Pengurangan skor per detik waktu yang dihabiskan.")]
    [SerializeField] private int scoreReductionPerSecond = 5;

    [Tooltip("Skor reward per NPC yang berhasil di-KO.")]
    [SerializeField] private int scorePerNpcKo = 2;

    [Tooltip("Skor reward jika Player HP penuh saat finish.")]
    [SerializeField] private int fullHpBonus = 10;

    [Tooltip("Skor reward per sisa HP Player (jika tidak penuh).")]
    [SerializeField] private int pointsPerHpRemaining = 1;

    [Tooltip("Skor reward jika durabilitas Trolley penuh saat finish.")]
    [SerializeField] private int fullDurabilityBonus = 10;

    [Tooltip("Skor reward per sisa durabilitas Trolley (jika tidak penuh).")]
    [SerializeField] private int pointsPerDurabilityRemaining = 1;

    // State Internal
    private float startTime;
    private float timeElapsed = 0f;
    private int npcKOs = 0;
    private bool isGameActive = true;

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR (Singleton Pattern):
        // Memastikan ScoreManager mudah diakses oleh NPCController (saat KO)
        // dan CashierGoal (saat menang) tanpa overhead performa.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // Catat waktu mulai permainan untuk menghitung durasi secara offline saat finish (menghemat overhead CPU)
        startTime = Time.time;
    }

    /// <summary>
    /// Mencatat peningkatan jumlah NPC yang berhasil di-KO oleh player.
    /// </summary>
    public void RegisterNpcKO()
    {
        if (!isGameActive) return;
        npcKOs++;
        Debug.Log($"[ScoreManager] NPC KO terdaftar! Total KO: {npcKOs}");
    }

    /// <summary>
    /// Mengakhiri perhitungan waktu, menghitung akumulasi skor, dan menampilkan visual breakdown pada panel You Win.
    /// </summary>
    /// <param name="winPanel">Referensi GameObject panel BGYouWin</param>
    public void EndGame(GameObject winPanel)
    {
        if (!isGameActive) return;
        isGameActive = false;

        // Hitung total durasi waktu game secara dinamis (offline calculation)
        timeElapsed = Time.time - startTime;

        // 1. Kalkulasi Bonus Waktu (Time-Based Score)
        int timeBonus = Mathf.Max(0, baseTimeScore - Mathf.FloorToInt(timeElapsed * scoreReductionPerSecond));

        // 2. Kalkulasi KO NPC (+2 per KO)
        int npcKoBonus = npcKOs * scorePerNpcKo;

        // 3. Kalkulasi Player HP (Full HP +10, otherwise +1 per sisa HP)
        int hpBonus = 0;
        int currentHp = 0;
        int maxHp = 5;

        if (HealthManager.Instance != null)
        {
            currentHp = HealthManager.Instance.CurrentPlayerHp;
            maxHp = HealthManager.Instance.MaxPlayerHp;
            if (currentHp == maxHp)
            {
                hpBonus = fullHpBonus;
            }
            else
            {
                hpBonus = currentHp * pointsPerHpRemaining;
            }
        }

        // 4. Kalkulasi Trolley Durability (Full Durability +10, otherwise +1 per sisa durabilitas)
        int durabilityBonus = 0;
        int currentDurability = 0;
        int maxDurability = 8;

        if (HealthManager.Instance != null)
        {
            currentDurability = HealthManager.Instance.CurrentTrolleyDurability;
            maxDurability = HealthManager.Instance.MaxTrolleyDurability;
            if (currentDurability == maxDurability)
            {
                durabilityBonus = fullDurabilityBonus;
            }
            else
            {
                durabilityBonus = currentDurability * pointsPerDurabilityRemaining;
            }
        }

        // 5. Total Skor Akhir
        int totalScore = timeBonus + npcKoBonus + hpBonus + durabilityBonus;

        Debug.Log($"[ScoreManager] Game Finished!\n" +
                  $"- Time Elapsed: {timeElapsed:F2}s (Bonus: {timeBonus})\n" +
                  $"- NPC KOs: {npcKOs} (Bonus: {npcKoBonus})\n" +
                  $"- Player HP: {currentHp}/{maxHp} (Bonus: {hpBonus})\n" +
                  $"- Durability: {currentDurability}/{maxDurability} (Bonus: {durabilityBonus})\n" +
                  $"- Total Score: {totalScore}");

        // 6. Tampilkan ke komponen TextMeshProUGUI pada panel You Win
        if (winPanel != null)
        {
            Transform scoreTransform = winPanel.transform.Find("Score");
            if (scoreTransform != null)
            {
                TextMeshProUGUI scoreText = scoreTransform.GetComponent<TextMeshProUGUI>();
                if (scoreText != null)
                {
                    // Menampilkan skor utama beserta detail breakdown yang estetik (ukuran font kecil & warna-warni)
                    scoreText.text = $"Your Score = {totalScore}\n" +
                                     $"<size=22><color=#FFFF77>Time Bonus:</color> {timeBonus} pts ({FormatTime(timeElapsed)})\n" +
                                     $"<color=#FF7777>NPC KO:</color> +{npcKoBonus} pts ({npcKOs} KOs)\n" +
                                     $"<color=#77FF77>Player HP:</color> +{hpBonus} pts\n" +
                                     $"<color=#77FFFF>Trolley Durability:</color> +{durabilityBonus} pts</size>";
                }
                else
                {
                    Debug.LogWarning("[ScoreManager] Komponen TextMeshProUGUI tidak ditemukan pada objek 'Score'!");
                }
            }
            else
            {
                Debug.LogWarning("[ScoreManager] Objek anak bernama 'Score' tidak ditemukan di BGYouWin!");
            }
        }
    }

    /// <summary>
    /// Memformat waktu detik menjadi format menit:detik yang mudah dibaca.
    /// </summary>
    private string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainingSeconds = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:00}:{remainingSeconds:00}";
    }
}
