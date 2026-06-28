using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Script ini berfungsi untuk mendeteksi objek dengan tag "Weapon" atau "Goods" yang masuk ke area trigger interaksi,
/// menghitung objek mana yang paling dekat dengan Trolley secara realtime, dan memberikan efek sorotan (outline highlight)
/// pada objek terdekat tersebut untuk meningkatkan User Experience (UX) pemain.
/// </summary>
public class TrolleyInteractController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform acuan untuk perhitungan jarak (biasanya bodi utama Trolley). Jika dikosongkan, akan menggunakan parent transform atau transform ini sendiri.")]
    [SerializeField] private Transform referenceTransform;

    [Header("Settings")]
    [Tooltip("Tag objek yang diperbolehkan masuk ke area interaksi.")]
    [SerializeField] private List<string> targetTags = new List<string> { "Goods" };

    // List internal untuk menampung semua objek target yang berada di dalam area trigger.
    // LOGIC DI BALIK LAYAR: Menggunakan hash set atau list untuk melacak objek aktif secara dinamis.
    // Setiap kali objek masuk (OnTriggerEnter), ia ditambahkan ke list ini, dan dihapus saat keluar (OnTriggerExit).
    private class CandidateInfo
    {
        public Outline outline;
        public ObjectScript objectScript;

        public CandidateInfo(Outline outline, ObjectScript objectScript)
        {
            this.outline = outline;
            this.objectScript = objectScript;
        }
    }

    // List internal untuk menampung semua objek target yang berada di dalam area trigger beserta referensi ObjectScript-nya yang ter-cache.
    private List<CandidateInfo> candidatesInArea = new List<CandidateInfo>();

    [Header("Optimasi WebGL Settings")]
    [Tooltip("Interval pembaruan (detik) untuk mengecek kandidat barang terdekat.")]
    [SerializeField] private float updateInterval = 0.1f;

    [Tooltip("Apakah sorotan outline visual pada barang di sekitar diaktifkan? (Disetel false untuk PUBG UI style).")]
    [SerializeField] private bool enableOutlineHighlight = false;

    // Cache objek terdekat (WebGL Mobile Optimization)
    private Outline closestCandidate = null;

    // Event yang dipicu ketika daftar barang di sekitar berubah (bertambah/berkurang)
    public System.Action OnCandidatesChanged;

    // ==========================================
    // Properties dan Method Publik untuk HUDController
    // ==========================================

    /// <summary>
    /// Mengekspos referensi ke objek terdekat yang sedang di-highlight saat ini secara dinamis (on-demand).
    /// Menggunakan cache internal O(1) yang diperbarui secara efisien.
    /// </summary>
    public Outline CurrentHighlightedOutline => closestCandidate;

    /// <summary>
    /// Jumlah kandidat barang belanjaan/senjata di sekitar area interaksi saat ini.
    /// </summary>
    public int CandidateCount
    {
        get
        {
            CleanupCandidates();
            return candidatesInArea.Count;
        }
    }

    /// <summary>
    /// Mengambil ObjectScript dari kandidat pada indeks tertentu secara aman (zero-allocation).
    /// </summary>
    public ObjectScript GetCandidateObjectScript(int index)
    {
        CleanupCandidates();
        if (index >= 0 && index < candidatesInArea.Count)
        {
            return candidatesInArea[index].objectScript;
        }
        return null;
    }

    /// <summary>
    /// Menghapus objek dari daftar kandidat secara manual (misal setelah objek di-grab pemain).
    /// </summary>
    public void RemoveCandidate(Outline outline)
    {
        if (outline == null) return;

        // Matikan outline jika opsi aktif
        if (enableOutlineHighlight)
        {
            outline.ToggleOutline(false);
        }

        bool removed = false;
        for (int i = 0; i < candidatesInArea.Count; i++)
        {
            if (candidatesInArea[i].outline == outline)
            {
                candidatesInArea.RemoveAt(i);
                removed = true;
                break;
            }
        }

        // Segera perbarui referensi terdekat setelah kandidat berkurang
        UpdateClosestCandidate();

        // Picu event perubahan list agar UI langsung ter-update
        if (removed)
        {
            OnCandidatesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Mencoba mendaftarkan kembali objek secara manual ke dalam list highlight (misalnya jika objek keluar dari keranjang
    /// dan ternyata fisiknya masih berada di dalam area jangkauan interaksi tangan).
    /// </summary>
    public void TryAddCandidate(Outline outline)
    {
        if (outline == null) return;

        // Pastikan tag objek diperbolehkan
        if (targetTags.Contains(outline.tag))
        {
            // Cek apakah objek sudah terdaftar
            foreach (var candidate in candidatesInArea)
            {
                if (candidate.outline == outline) return;
            }

            // Cek apakah objek sudah berstatus Taken (di dalam trolley/tangan)
            ObjectScript objectScript = outline.GetComponent<ObjectScript>();
            if (objectScript == null) objectScript = outline.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = outline.GetComponentInChildren<ObjectScript>();

            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                return; // Jangan ditambahkan jika statusnya Taken
            }

            candidatesInArea.Add(new CandidateInfo(outline, objectScript));
            if (enableOutlineHighlight)
            {
                outline.ToggleOutline(true);
            }

            // Segera perbarui referensi terdekat setelah kandidat bertambah
            UpdateClosestCandidate();

            // Picu event perubahan list agar UI langsung ter-update
            OnCandidatesChanged?.Invoke();
        }
    }

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR:
        // Jika referenceTransform belum diatur di Inspector, cari parent terdekat sebagai acuan titik pusat.
        // Jika tidak memiliki parent, gunakan transform objek ini sendiri. Hal ini mempermudah pemasangan komponen
        // tanpa memaksa desainer level mengisi field referenceTransform secara manual.
        if (referenceTransform == null)
        {
            referenceTransform = transform.parent != null ? transform.parent : transform;
        }

        // Jalankan coroutine pemantauan berkala untuk mencari barang terdekat (menghemat Update CPU)
        StartCoroutine(FindClosestCandidateCoroutine());
    }

    /// <summary>
    /// Coroutine berkala untuk memantau dan memperbarui kandidat barang terdekat.
    /// Mengurangi beban Update setiap frame pada Mobile WebGL.
    /// </summary>
    private System.Collections.IEnumerator FindClosestCandidateCoroutine()
    {
        // Cache yield instruction untuk mencegah alokasi GC (Garbage Collection) di Mobile WebGL
        WaitForSeconds delay = new WaitForSeconds(updateInterval);

        while (true)
        {
            yield return delay;

            // HANYA jalankan perhitungan jarak jika terdapat lebih dari 1 kandidat.
            // Jika hanya ada 1 kandidat, dia otomatis menjadi yang terdekat (tanpa perhitungan).
            if (candidatesInArea.Count > 1)
            {
                UpdateClosestCandidate();
            }
        }
    }

    /// <summary>
    /// Memperbarui referensi objek terdekat (closestCandidate) menggunakan sqrMagnitude (WebGL Mobile optimization).
    /// </summary>
    private void UpdateClosestCandidate()
    {
        CleanupCandidates();

        int count = candidatesInArea.Count;
        if (count == 0)
        {
            closestCandidate = null;
            return;
        }

        if (count == 1)
        {
            closestCandidate = candidatesInArea[0].outline;
            return;
        }

        Outline closest = null;
        float minSqrDistance = float.MaxValue;
        Vector3 refPos = referenceTransform.position;

        for (int i = 0; i < count; i++)
        {
            var candidate = candidatesInArea[i];
            if (candidate.outline == null) continue;

            // OPTIMALISASI FISIKA: Gunakan sqrMagnitude untuk menghindari operasi akar kuadrat (Mathf.Sqrt)
            Vector3 diff = refPos - candidate.outline.transform.position;
            float sqrDist = diff.sqrMagnitude;

            if (sqrDist < minSqrDistance)
            {
                minSqrDistance = sqrDist;
                closest = candidate.outline;
            }
        }

        closestCandidate = closest;
    }

    /// <summary>
    /// Dipanggil oleh Unity Physics Engine ketika collider lain memasuki area Trigger objek ini.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // LOGIC DI BALIK LAYAR:
        // Kita mencocokkan tag objek yang masuk. Jika tag-nya ada di daftar targetTags,
        // kita coba mendapatkan script 'Outline' pada objek tersebut (bisa langsung di objek itu, di parent, atau di child-nya).
        if (targetTags.Contains(other.tag))
        {
            Outline outline = other.GetComponent<Outline>();
            
            // Fallback: Jika Outline tidak ditemukan langsung di collider (misal collider berada di child gameobject),
            // cari di parent atau child objek tersebut.
            if (outline == null) outline = other.GetComponentInParent<Outline>();
            if (outline == null) outline = other.GetComponentInChildren<Outline>();

            // Jika script Outline ditemukan, cari ObjectScript-nya untuk di-cache sekali saja saat terdeteksi
            if (outline != null)
            {
                // Cek apakah objek sudah terdaftar
                foreach (var candidate in candidatesInArea)
                {
                    if (candidate.outline == outline) return;
                }

                ObjectScript objectScript = other.GetComponent<ObjectScript>();
                if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
                if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

                // Jangan tambahkan jika statusnya sudah Taken
                if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
                {
                    return;
                }

                candidatesInArea.Add(new CandidateInfo(outline, objectScript));
                if (enableOutlineHighlight)
                {
                    outline.ToggleOutline(true); // Langsung nyalakan sorotan outline saat barang masuk area jangkauan
                }

                // Segera perbarui referensi terdekat setelah kandidat bertambah
                UpdateClosestCandidate();

                // Picu event perubahan list agar UI langsung ter-update
                OnCandidatesChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Dipanggil oleh Unity Physics Engine ketika collider lain keluar dari area Trigger objek ini.
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        if (targetTags.Contains(other.tag))
        {
            Outline outline = other.GetComponent<Outline>();
            
            if (outline == null) outline = other.GetComponentInParent<Outline>();
            if (outline == null) outline = other.GetComponentInChildren<Outline>();

            if (outline != null)
            {
                // DETEKSI ONTRIGGEREXIT PALSU (PENTING UNTUK WEBGL MOBILE OPTIMIZATION):
                // Ketika Rigidbody objek di-set isKinematic = true dan useGravity = false (tidur untuk hemat CPU),
                // Unity secara otomatis menembakkan OnTriggerExit palsu meskipun objek secara fisik masih berada di dalam area trigger.
                // Kita verifikasi jarak nyata objek ke trolley menggunakan sqrMagnitude (zero Sqrt overhead).
                float sqrDistance = (referenceTransform.position - outline.transform.position).sqrMagnitude;
                float maxAllowedDistance = GetInteractAreaRadius();
                float maxAllowedDistanceSqr = maxAllowedDistance * maxAllowedDistance;
                
                if (sqrDistance < maxAllowedDistanceSqr)
                {
                    return; // Objek masih di dalam jangkauan fisik, abaikan pemicu exit palsu
                }

                // Cari dan hapus dari list candidates
                bool removed = false;
                for (int i = candidatesInArea.Count - 1; i >= 0; i--)
                {
                    if (candidatesInArea[i].outline == outline)
                    {
                        candidatesInArea.RemoveAt(i);
                        if (enableOutlineHighlight)
                        {
                            outline.ToggleOutline(false); // Matikan highlight jika opsi aktif
                        }
                        removed = true;
                    }
                }

                // Segera perbarui referensi terdekat setelah kandidat berkurang
                UpdateClosestCandidate();

                // Picu event perubahan list agar UI langsung ter-update
                if (removed)
                {
                    OnCandidatesChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Menghitung radius atau batas jangkauan terluar dari Collider Trigger interaksi ini.
    /// </summary>
    private float GetInteractAreaRadius()
    {
        Collider myCollider = GetComponent<Collider>();
        if (myCollider == null) return 5f;

        // Dapatkan skala maksimum bodi untuk mendukung scaling tidak seragam
        float maxScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);

        if (myCollider is SphereCollider sphere)
        {
            return sphere.radius * maxScale;
        }
        else if (myCollider is BoxCollider box)
        {
            // Gunakan setengah dari diagonal box collider sebagai radius representatif
            return box.size.magnitude * 0.5f * maxScale;
        }
        else if (myCollider is CapsuleCollider capsule)
        {
            return capsule.height * 0.5f * maxScale;
        }

        return 5f;
    }

    /// <summary>
    /// Membersihkan daftar kandidat dari objek yang sudah bernilai null (misal karena telah dipickup dan dihancurkan).
    /// </summary>
    private void CleanupCandidates()
    {
        // Loop mundur (dari belakang ke depan) sangat penting ketika menghapus elemen dari list di dalam loop.
        for (int i = candidatesInArea.Count - 1; i >= 0; i--)
        {
            CandidateInfo candidate = candidatesInArea[i];
            if (candidate.outline == null || !candidate.outline.gameObject.activeInHierarchy)
            {
                candidatesInArea.RemoveAt(i);
                continue;
            }

            // APABILA barang statusnya berubah menjadi 'Taken' (berhasil masuk trolley atau di tangan),
            // segera matikan outline dan keluarkan dari list kandidat.
            if (candidate.objectScript != null && candidate.objectScript.Status == ObjectStatus.Taken)
            {
                candidate.outline.ToggleOutline(false);
                candidatesInArea.RemoveAt(i);
            }
        }
    }

    // Dipanggil saat komponen dinonaktifkan untuk merapikan sisa highlight yang masih aktif.
    private void OnDisable()
    {
        for (int i = 0; i < candidatesInArea.Count; i++)
        {
            if (candidatesInArea[i].outline != null)
            {
                candidatesInArea[i].outline.ToggleOutline(false);
            }
        }
        candidatesInArea.Clear();
    }
}
