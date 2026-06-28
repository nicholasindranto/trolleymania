using UnityEngine;

/// <summary>
/// Script ini ditempelkan secara dinamis pada child objek 'area' dari setiap prefab supermarket object.
/// Berfungsi untuk mendeteksi interaksi/tabrakan awal dan membangunkan Rigidbody dari kondisi tidur (isKinematic)
/// untuk menghemat performa CPU pada WebGL Mobile.
/// </summary>
public class ObjectAreaTrigger : MonoBehaviour
{
    [SerializeField] private ObjectScript parentScript;

    /// <summary>
    /// Menginisialisasi referensi ke ObjectScript induk.
    /// </summary>
    // public void Initialize(ObjectScript parent)
    // {
    //     parentScript = parent;
    // }

    private void OnTriggerEnter(Collider other)
    {
        if (parentScript == null) return;

        // Memeriksa apakah objek yang masuk memiliki tag yang dapat memicu interaksi fisik
        if (other.CompareTag("Player") || 
            other.CompareTag("Goods") || 
            other.CompareTag("NPCTrolley") || 
            other.CompareTag("PlayerTrolley"))
        {
            // Bangunkan Rigidbody induk untuk memulai simulasi fisika
            parentScript.WakeUp();
        }
    }
}
