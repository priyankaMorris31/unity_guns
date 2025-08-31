using System.Collections;
using UnityEngine;
using Photon.Pun;

public class GunSwitcher : MonoBehaviourPun, IPunObservable
{
    [Header("Optional - parents to auto-populate (or leave empty and assign arrays manually)")]
    public Transform fpsGunsParent;   // e.g. FPSMainCamera transform
    public Transform worldGunsParent; // e.g. hand/Alpha_Surface transform

    [Header("Optional - manual assignment")]
    public GameObject[] fpsGuns;
    public GameObject[] worldGuns;

    private int currentGunIndex = 0;
    private bool started = false;

    void Awake()
    {
        // Auto-fill arrays from parents if not assigned in inspector
        if ((fpsGuns == null || fpsGuns.Length == 0) && fpsGunsParent != null)
            FillFromParent(fpsGunsParent, ref fpsGuns);

        if ((worldGuns == null || worldGuns.Length == 0) && worldGunsParent != null)
            FillFromParent(worldGunsParent, ref worldGuns);
    }

    void Start()
    {
        // Apply initial state (owner: fps + world, remote: world only)
        ApplyGunIndex(currentGunIndex, photonView.IsMine);
        started = true;
    }

    void FillFromParent(Transform parent, ref GameObject[] array)
    {
        int c = parent.childCount;
        array = new GameObject[c];
        for (int i = 0; i < c; i++) array[i] = parent.GetChild(i).gameObject;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // Guard: no fps guns -> nothing to switch
        if (fpsGuns == null || fpsGuns.Length == 0) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SetGunIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetGunIndex(1);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f) SetGunIndex((currentGunIndex + 1) % fpsGuns.Length);
        if (scroll < 0f) SetGunIndex((currentGunIndex - 1 + fpsGuns.Length) % fpsGuns.Length);
    }

    void SetGunIndex(int index)
    {
        // clamp to a safe range (we allow different lengths — missing elements are ignored)
        if (fpsGuns != null && fpsGuns.Length > 0) index = Mathf.Clamp(index, 0, fpsGuns.Length - 1);
        if (worldGuns != null && worldGuns.Length > 0) index = Mathf.Clamp(index, 0, Mathf.Max(0, worldGuns.Length - 1));

        if (index == currentGunIndex) return;

        currentGunIndex = index;

        // Apply locally right away (owner: show FPS + world); remote clients will learn the index via network
        ApplyGunIndex(currentGunIndex, true);
        // No RPC needed — IPunObservable will send the index next network tick
    }

    // index -> which gun to enable. isOwner = true means enable fpsGuns for the owner too.
    void ApplyGunIndex(int index, bool isOwner)
    {
        // FPS guns (owner only)
        if (fpsGuns != null)
        {
            for (int i = 0; i < fpsGuns.Length; i++)
            {
                if (fpsGuns[i] == null) continue;
                fpsGuns[i].SetActive(isOwner && i == index);
            }
        }

        // World/TPS guns (everyone)
        if (worldGuns != null)
        {
            for (int i = 0; i < worldGuns.Length; i++)
            {
                if (worldGuns[i] == null) continue;
                worldGuns[i].SetActive(i == index);
            }
        }
    }

    // Photon network sync for late joiners / continuous sync.
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting && photonView.IsMine)
        {
            stream.SendNext(currentGunIndex);
        }
        else if (stream.IsReading)
        {
            int incoming = (int)stream.ReceiveNext();
            // Remote side: apply the world-only view. If Start hasn't completed yet, defer one frame.
            currentGunIndex = incoming;
            if (started)
            {
                ApplyGunIndex(incoming, false);
            }
            else
            {
                StartCoroutine(DeferredApply(incoming));
            }
        }
    }

    IEnumerator DeferredApply(int index)
    {
        yield return null; // wait a frame so Start/Awake run and arrays are populated
        ApplyGunIndex(index, false);
    }
}
