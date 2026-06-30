using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Script ini berfungsi untuk mengontrol Floating/Dynamic Joystick pada game mobile.
/// Menggunakan sistem EventSystems bawaan Unity agar responsif terhadap sentuhan jari (Touch) maupun klik mouse (Mouse Click).
/// </summary>
public class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("UI References")]
    [Tooltip("Area sentuh transparan di sebelah kiri layar (Joystick Touch Area).")]
    [SerializeField] private RectTransform joystickZone;

    [Tooltip("Background lingkaran luar joystick (Joystick Background).")]
    [SerializeField] private RectTransform background;

    [Tooltip("Handle/tombol kecil di tengah joystick (Joystick Handle).")]
    [SerializeField] private RectTransform handle;

    [Header("Settings")]
    [Tooltip("Jarak maksimum handle bisa bergeser dari pusat background (dalam unit piksel UI).")]
    [SerializeField] private float dragRange = 100f;

    // Menyimpan nilai input arah joystick (nilai X dan Y akan berada di antara -1 hingga 1)
    private Vector2 input = Vector2.zero;

    // Menyimpan posisi default awal joystick dari Inspector (misalnya -170, -230)
    private Vector2 defaultPosition;

    /// <summary>
    /// Property publik agar script lain (seperti script pergerakan karakter) 
    /// bisa membaca arah pergerakan joystick secara real-time.
    /// </summary>
    public Vector2 Direction => ObjectiveManager.IsLoading ? Vector2.zero : input;
    public float Horizontal => ObjectiveManager.IsLoading ? 0f : input.x;
    public float Vertical => ObjectiveManager.IsLoading ? 0f : input.y;

    private void Start()
    {
        // Jika joystickZone tidak di-assign secara manual di Inspector,
        // cari secara otomatis RectTransform dari GameObject tempat script ini menempel.
        if (joystickZone == null)
        {
            joystickZone = GetComponent<RectTransform>();
        }

        // Simpan posisi default awal yang di-set di Inspector agar kita bisa mengembalikannya
        // ke posisi ini saat jari dilepas.
        if (background != null)
        {
            defaultPosition = background.anchoredPosition;
        }
    }

    /// <summary>
    /// Dipanggil otomatis oleh Unity EventSystem saat pertama kali jari menyentuh area joystickZone.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // KODENYA TERSPESIALISASI: Blokir sentuhan joystick jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (background == null || handle == null) return;

        // Ubah posisi sentuhan layar ke koordinat World Space di Canvas.
        // LOGIC DI BALIK LAYAR / ALASAN POSISI TIDAK AKURAT SEBELUMNYA:
        // Kemungkinan besar Pivot dari JoystickTouchArea (parent) berbeda dengan Anchor dari JoystickBG (child).
        // - JoystickTouchArea menggunakan Anchor Left-Stretch (Pivot X biasanya di 0 / sisi paling kiri).
        // - JoystickBG menggunakan Anchor Center (Pivot X di 0.5 / tengah).
        // Ketika kita menggunakan ScreenPointToLocalPointInRectangle dengan referensi joystickZone,
        // nilai lokal yang dihasilkan dihitung dari Pivot joystickZone (sisi kiri layar).
        // Tapi begitu dimasukkan ke background.anchoredPosition, posisinya bergeser karena background diposisikan
        // relatif terhadap Anchor-nya sendiri (tengah). Ini yang membuat posisinya melenceng ke kanan.
        //
        // SOLUSI:
        // Kita gunakan RectTransformUtility.ScreenPointToWorldPointInRectangle dengan referensi parent dari background.
        // Fungsi ini mengabaikan perbedaan pivot/anchor internal dan langsung mendapatkan koordinat World Space
        // di mana klik terjadi pada Canvas. Kita tinggal menetapkan background.position = worldPoint.
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
            background.parent as RectTransform, 
            eventData.position, 
            eventData.pressEventCamera, 
            out Vector3 worldPoint))
        {
            // Pindahkan posisi world joystick tepat di bawah kursor mouse/jari pemain
            background.position = worldPoint;
        }

        // Reset posisi handle ke pusat (0, 0) dari background pada awal sentuhan.
        handle.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Dipanggil otomatis oleh Unity EventSystem setiap kali jari bergeser (drag) di layar.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        // KODENYA TERSPESIALISASI: Blokir gerakan joystick jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (background == null || handle == null) return;

        // 1. Dapatkan posisi jari saat ini relatif terhadap pusat Joystick Background.
        // LOGIC DI BALIK LAYAR:
        // Dengan mengoper 'background' sebagai parameter RectTransform, koordinat (0,0) dari 'localPoint'
        // yang dihasilkan akan tepat berada di tengah-tengah lingkaran background joystick.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background, 
            eventData.position, 
            eventData.pressEventCamera, 
            out Vector2 localPoint))
        {
            // 2. Hitung jarak kuadrat dari titik pusat ke posisi jari saat ini menggunakan sqrMagnitude (zero Sqrt overhead).
            float sqrDistance = localPoint.sqrMagnitude;
            float dragRangeSqr = dragRange * dragRange;

            // 3. Batasi pergerakan handle agar tidak keluar melebihi batas lingkaran luar (dragRange).
            if (sqrDistance > dragRangeSqr)
            {
                // Posisikan handle tepat di batas maksimum lingkaran.
                Vector2 direction = localPoint.normalized;
                handle.anchoredPosition = direction * dragRange;
            }
            else
            {
                // Jika jari masih di dalam batas dragRange, posisikan handle tepat di posisi jari.
                handle.anchoredPosition = localPoint;
            }

            // 4. Hitung nilai input normalisasi (di antara -1 dan 1) untuk dibaca oleh script karakter.
            // LOGIC DI BALIK LAYAR:
            // Posisi handle saat ini (misal X: 50, Y: -30) dibagi dengan batas maksimal dragRange (misal 100).
            // Hasilnya adalah Vector2 dengan nilai X: 0.5 dan Y: -0.3. Ini adalah nilai input standar analog joystick.
            input = handle.anchoredPosition / dragRange;
        }
    }

    /// <summary>
    /// Dipanggil otomatis oleh Unity EventSystem ketika jari diangkat dari layar.
    /// </summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        // KODENYA TERSPESIALISASI: Blokir pelepasan joystick jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (background == null || handle == null) return;

        // Kembalikan posisi background joystick ke posisi default-nya semula (-170, -230)
        background.anchoredPosition = defaultPosition;

        // Reset nilai input ke nol (Vector2.zero) agar karakter langsung berhenti bergerak.
        input = Vector2.zero;

        // Reset posisi handle ke pusat lingkaran background.
        handle.anchoredPosition = Vector2.zero;
    }
}
