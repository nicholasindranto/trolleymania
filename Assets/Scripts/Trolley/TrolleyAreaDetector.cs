using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Script ini ditempelkan pada objek 'TrolleyArea' yang memiliki BoxCollider bertipe IsTrigger.
/// Berfungsi untuk mendeteksi barang belanjaan (Goods) yang masuk ke dalam keranjang trolley,
/// merubah statusnya menjadi Taken (sehingga tidak bisa di-highlight lagi), dan menjumlahkan beratnya ke TrolleyController.
/// </summary>
public class TrolleyAreaDetector : MonoBehaviour
{
    // Menyimpan referensi ke TrolleyController untuk menambahkan/mengurangi berat secara dinamis.
    private TrolleyController trolleyController;

    // Menyimpan daftar barang belanjaan yang saat ini berada di dalam keranjang trolley.
    private List<ObjectScript> itemsInTrolley = new List<ObjectScript>();

    // Event yang dipicu ketika daftar barang di dalam trolley berubah (ditambah/dikurang)
    public System.Action OnTrolleyItemsChanged;

    /// <summary>
    /// Mendapatkan list objek yang berada di dalam trolley.
    /// </summary>
    public List<ObjectScript> ItemsInTrolley => itemsInTrolley;

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR:
        // Karena objek 'TrolleyArea' adalah anak langsung dari 'Trolley', kita dapat mencari
        // komponen TrolleyController di objek parent secara otomatis agar tidak memerlukan setup manual di Inspector.
        trolleyController = GetComponentInParent<TrolleyController>();

        if (trolleyController == null)
        {
            Debug.LogError("TrolleyAreaDetector: TrolleyController tidak ditemukan pada parent GameObject!");
        }
    }

    /// <summary>
    /// Dipanggil saat suatu objek memasuki area keranjang Trolley.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // 1. Pastikan objek yang masuk memiliki tag "Goods"
        if (other.CompareTag("Goods"))
        {
            ObjectScript objectScript = other.GetComponent<ObjectScript>();
            
            // Fallback jika collider ada pada child object
            if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

            // 2. Jika memiliki ObjectScript dan statusnya masih Grounded (di luar trolley)
            if (objectScript != null && objectScript.Status == ObjectStatus.Grounded)
            {
                // Ubah status menjadi Taken agar sistem interaksi skip objek ini dari highlight
                objectScript.Status = ObjectStatus.Taken;

                // Tambahkan ke daftar barang di dalam trolley
                if (!itemsInTrolley.Contains(objectScript))
                {
                    itemsInTrolley.Add(objectScript);
                    OnTrolleyItemsChanged?.Invoke();
                }

                // Tambahkan berat barang ke total berat di TrolleyController
                if (trolleyController != null)
                {
                    trolleyController.currentWeight += objectScript.ObjWeight;
                    Debug.Log($"[Trolley] Barang '{objectScript.ObjName}' ditambahkan. Berat barang: {objectScript.ObjWeight} | Total Berat Trolley: {trolleyController.currentWeight}");
                }

                // LOGIC DI BALIK LAYAR (Pembaruan Progres Belanjaan):
                // Laporkan pertambahan barang ke ObjectiveManager hanya jika objek bertipe Goods
                if (other.CompareTag("Goods") && ObjectiveManager.Instance != null)
                {
                    ObjectiveManager.Instance.UpdateObjectiveProgress(objectScript.ObjName, 1);
                }
            }
        }
    }

    /// <summary>
    /// Dipanggil saat suatu objek keluar/terpental dari area keranjang Trolley.
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        // 1. Pastikan objek yang keluar memiliki tag "Goods"
        if (other.CompareTag("Goods"))
        {
            ObjectScript objectScript = other.GetComponent<ObjectScript>();

            if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

            // 2. Jika memiliki ObjectScript dan statusnya Taken (berada di dalam trolley)
            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                // OPTIMALISASI FISIKA WEBGL:
                // Unity Engine memicu event OnTriggerExit palsu ketika sebuah Rigidbody diganti statusnya menjadi kinematic (isKinematic = true)
                // saat ditidurkan di dalam trolley. Kita harus menyaring event palsu ini dengan mendeteksi isKinematic.
                Rigidbody itemRb = other.GetComponent<Rigidbody>();
                if (itemRb == null) itemRb = other.GetComponentInParent<Rigidbody>();
                if (itemRb == null) itemRb = other.GetComponentInChildren<Rigidbody>();

                if (itemRb != null && itemRb.isKinematic)
                {
                    return; // Abaikan event exit palsu karena objek hanya ditidurkan, bukan keluar secara fisik
                }

                // Kembalikan status menjadi Grounded agar bisa diinteraksi kembali jika berada di tanah
                objectScript.Status = ObjectStatus.Grounded;

                // Hapus dari daftar barang di dalam trolley
                itemsInTrolley.Remove(objectScript);
                OnTrolleyItemsChanged?.Invoke();

                // Kurangi berat barang dari total berat di TrolleyController
                if (trolleyController != null)
                {
                    // Menggunakan Mathf.Max(0f, ...) untuk mencegah nilai negatif akibat presisi float (floating-point precision error)
                    trolleyController.currentWeight = Mathf.Max(0f, trolleyController.currentWeight - objectScript.ObjWeight);
                    Debug.Log($"[Trolley] Barang '{objectScript.ObjName}' keluar/dikeluarkan. Berat barang: {objectScript.ObjWeight} | Total Berat Trolley: {trolleyController.currentWeight}");

                    // LOGIC DI BALIK LAYAR (Pengecekan Tumpang Tindih Area Grab):
                    // Ketika barang belanjaan jatuh keluar dari keranjang trolley, statusnya berubah menjadi Grounded (bisa diambil).
                    // Namun karena posisinya mungkin masih berada secara fisik di dalam area jangkauan tangan/trolley (InteractArea),
                    // event OnTriggerEnter dari InteractArea tidak akan terpicu lagi (karena collidernya tidak pernah "keluar lalu masuk lagi").
                    // Solusi: Kita lakukan uji posisi instant menggunakan `Collider.ClosestPoint` pada collider milik InteractArea.
                    // Jika jarak titik terdekat kurang dari ambang batas toleransi, daftarkan kembali objek ke list highlight secara manual.
                    TrolleyInteractController interactController = trolleyController.GetComponentInChildren<TrolleyInteractController>();
                    if (interactController != null)
                    {
                        Collider interactCollider = interactController.GetComponent<Collider>();
                        if (interactCollider != null)
                        {
                            // ClosestPoint mengembalikan koordinat permukaan terdekat atau koordinat objek itu sendiri jika berada di dalam volume.
                            Vector3 closestPoint = interactCollider.ClosestPoint(other.transform.position);
                            float sqrDistanceToCollider = (closestPoint - other.transform.position).sqrMagnitude;

                            // Jika jaraknya hampir 0 (sangat dekat/di dalam), daftarkan ulang sebagai kandidat grab/highlight
                            if (sqrDistanceToCollider < 0.0025f)
                            {
                                Outline outline = other.GetComponent<Outline>();
                                if (outline == null) outline = other.GetComponentInParent<Outline>();
                                if (outline == null) outline = other.GetComponentInChildren<Outline>();

                                if (outline != null)
                                {
                                    interactController.TryAddCandidate(outline);
                                }
                            }
                        }
                    }
                }

                // LOGIC DI BALIK LAYAR (Pengurangan Progres Belanjaan):
                // Laporkan pengurangan barang belanjaan ke ObjectiveManager hanya jika objek bertipe Goods
                if (other.CompareTag("Goods") && ObjectiveManager.Instance != null)
                {
                    ObjectiveManager.Instance.UpdateObjectiveProgress(objectScript.ObjName, -1);
                }
            }
        }
    }

    /// <summary>
    /// Secara eksplisit mengeluarkan barang dari trolley (misal saat dipindahkan ke tangan/di-equip).
    /// </summary>
    public void RemoveItemFromTrolley(ObjectScript item)
    {
        if (item == null) return;

        if (itemsInTrolley.Contains(item))
        {
            itemsInTrolley.Remove(item);
            OnTrolleyItemsChanged?.Invoke();

            // Kurangi berat barang dari total berat di TrolleyController
            if (trolleyController != null)
            {
                trolleyController.currentWeight = Mathf.Max(0f, trolleyController.currentWeight - item.ObjWeight);
                Debug.Log($"[Trolley] Barang '{item.ObjName}' dikeluarkan secara paksa (di-equip). Berat: {item.ObjWeight} | Total Berat Trolley: {trolleyController.currentWeight}");
            }

            // Laporkan pengurangan barang belanjaan ke ObjectiveManager
            if (item.CompareTag("Goods") && ObjectiveManager.Instance != null)
            {
                ObjectiveManager.Instance.UpdateObjectiveProgress(item.ObjName, -1);
            }
        }
    }

    /// <summary>
    /// Membangunkan semua objek di dalam trolley secara fisik dan memberikan dorongan acak (random impulse) ke atas/samping.
    /// Dipanggil saat trolley mengalami benturan/tabrakan keras.
    /// </summary>
    /// <param name="forceMagnitude">Kekuatan gaya impulse yang diberikan.</param>
    public void ShakeObjects(float forceMagnitude)
    {
        // Bersihkan objek yang mungkin sudah terhapus (destroy) demi menghindari NullReferenceException
        itemsInTrolley.RemoveAll(item => item == null);

        for (int i = 0; i < itemsInTrolley.Count; i++)
        {
            ObjectScript obj = itemsInTrolley[i];
            if (obj != null)
            {
                // Bangunkan Rigidbody terlebih dahulu dan ijinkan simulasi fisika sementara
                obj.WakeUpFromTrolleyHit();

                Rigidbody objRb = obj.GetComponent<Rigidbody>();
                if (objRb != null)
                {
                    // Arah dorongan acak: didominasi ke arah atas (Y), dengan sedikit kemiringan acak ke sumbu X & Z
                    float randomX = Random.Range(-0.4f, 0.4f);
                    float randomZ = Random.Range(-0.4f, 0.4f);
                    float randomY = Random.Range(0.8f, 1.3f); // Dominasi gaya ke atas agar berpotensi mental keluar

                    Vector3 forceDirection = new Vector3(randomX, randomY, randomZ).normalized;
                    objRb.AddForce(forceDirection * forceMagnitude, ForceMode.Impulse);
                }
            }
        }
    }
}
