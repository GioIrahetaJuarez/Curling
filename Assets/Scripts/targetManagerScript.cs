using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class targetManagerScript : MonoBehaviour
{
    [Header("References")]
    public GameObject targetPrefab;           // visual target prefab
    public Transform playerTransform;         // assign player transform in inspector

    [Header("Spawn Rules")]
    public float minDistanceFromPlayer = 3f;  // target must be at least this far from player
    [Tooltip("Viewport margin (0..0.5) to keep target inside screen. e.g. 0.05")]
    public float viewportMargin = 0.05f;
    public int spawnRetryAttempts = 20;       // attempts to find a valid spawn pos

    [Header("Success / Clearing")]
    public float successRadius = 0.6f;        // how close a puck must be to count as landed
    public float stopVelocityThreshold = 0.05f; // puck must be (nearly) stopped
    public float checkInterval = 0.2f;        // how often manager checks for success

    [Header("Behavior")]
    public bool spawnOnStart = true;

    // Only one target at a time
    GameObject currentTarget;
    Camera mainCam;
    Coroutine monitorCoroutine;

    void Start()
    {
        mainCam = Camera.main;
        if (spawnOnStart)
            SpawnNewTarget();
    }

    void Update()
    {
        // Use the new Input System: press R to force spawn a new target
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
            SpawnNewTarget();
    }

    // Public: ask manager to spawn a new target (will remove existing)
    public void SpawnNewTarget()
    {
        if (targetPrefab == null)
        {
            Debug.LogWarning("targetManagerScript: targetPrefab not assigned.");
            return;
        }

        // remove existing target if any
        if (currentTarget != null)
            Destroy(currentTarget);

        Vector3 spawnPos;
        bool found = TryFindSpawnPosition(out spawnPos);

        if (!found)
        {
            // fallback: place at some offset from player or at center of camera view
            if (playerTransform != null)
                spawnPos = playerTransform.position + (Vector3.right * minDistanceFromPlayer);
            else if (mainCam != null)
                spawnPos = mainCam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
            else
                spawnPos = Vector3.zero;
        }

        currentTarget = Instantiate(targetPrefab, spawnPos, Quaternion.identity);
        // Ensure the target is purely visual:
        var rb = currentTarget.GetComponent<Rigidbody2D>();
        if (rb != null) Destroy(rb);
        var rb3 = currentTarget.GetComponent<Rigidbody>();
        if (rb3 != null) Destroy(rb3);

        // start monitoring pucks if not already
        if (monitorCoroutine != null) StopCoroutine(monitorCoroutine);
        monitorCoroutine = StartCoroutine(MonitorPucks());
    }

    // Attempts to find a valid world position inside camera viewport and at least minDistanceFromPlayer away
    bool TryFindSpawnPosition(out Vector3 outPos)
    {
        outPos = Vector3.zero;
        if (mainCam == null)
            mainCam = Camera.main;

        if (mainCam == null)
            return false;

        for (int i = 0; i < spawnRetryAttempts; i++)
        {
            float vx = Random.Range(viewportMargin, 1f - viewportMargin);
            float vy = Random.Range(viewportMargin, 1f - viewportMargin);

            Vector3 world = mainCam.ViewportToWorldPoint(new Vector3(vx, vy, Mathf.Abs(mainCam.transform.position.z)));
            world.z = 0f;

            if (playerTransform != null)
            {
                if (Vector2.Distance(world, playerTransform.position) < minDistanceFromPlayer)
                    continue;
            }

            outPos = world;
            return true;
        }

        return false;
    }

    IEnumerator MonitorPucks()
    {
        // Notes: This implementation expects puck GameObjects to be tagged "Puck".
        // Tag your puck prefab with the tag "Puck" in the inspector for the manager to detect them.
        while (currentTarget != null)
        {
            // find all pucks in scene
            GameObject[] pucks = GameObject.FindGameObjectsWithTag("Puck");
            Vector2 targetPos = currentTarget.transform.position;

            foreach (var p in pucks)
            {
                if (p == null) continue;

                float dist = Vector2.Distance(p.transform.position, targetPos);
                if (dist <= successRadius)
                {
                    // check if puck has (almost) stopped
                    Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
                    bool stopped = (rb == null) || (rb.linearVelocity.sqrMagnitude <= stopVelocityThreshold * stopVelocityThreshold);

                    if (stopped)
                    {
                        OnTargetHit();
                        yield break; // stop monitoring until new target is spawned
                    }
                }
            }

            yield return new WaitForSeconds(checkInterval);
        }
    }

    void OnTargetHit()
    {
        // Clear all pucks (visual + physics) currently in scene that are tagged "Puck"
        var pucks = GameObject.FindGameObjectsWithTag("Puck");
        foreach (var p in pucks)
        {
            if (p != null) Destroy(p);
        }

        // remove current target and spawn a new one
        if (currentTarget != null) Destroy(currentTarget);
        currentTarget = null;

        // spawn a new target after a short delay so player sees the result
        Invoke(nameof(SpawnNewTarget), 0.5f);
    }

    // Optional: editor helper to force respawn
    [ContextMenu("ForceSpawnTarget")]
    void ForceSpawnTarget() => SpawnNewTarget();
}
