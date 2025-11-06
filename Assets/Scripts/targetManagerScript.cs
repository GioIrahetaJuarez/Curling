using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.Controls; // added for ButtonControl.wasPressedThisFrame

public class targetManagerScript : MonoBehaviour
{
    [Header("References")]
    public GameObject targetPrefab;           // visual target prefab
    public Transform playerTransform;         // assign player transform in inspector
    public GameObject puckPrefab;             // prefab for pucks to spawn around target

    [Header("Spawn Rules")]
    public float minDistanceFromPlayer = 3f;  // target must be at least this far from player
    [Tooltip("Viewport margin (0..0.5) to keep target inside screen. e.g. 0.05")]
    public float viewportMargin = 0.05f;
    public int spawnRetryAttempts = 20;       // attempts to find a valid spawn pos

    [Header("Puck Spawn")]
    public int minPucks = 3;
    public int maxPucks = 5;
    public float puckSpawnRadiusMin = 0.5f;
    public float puckSpawnRadiusMax = 1.5f;

    [Header("Success / Clearing")]
    public float successRadius = 0.6f;        // how close a puck must be to count as landed
    public float stopVelocityThreshold = 0.05f; // puck must be (nearly) stopped
    public float checkInterval = 0.2f;        // how often manager checks for success

    [Header("Behavior")]
    public bool spawnOnStart = true;

    [Header("Game Goals / UI")]
    public int targetsToHit = 5;              // goal: hit this many targets
    public TMP_Text targetsLeftText;          // assign a TextMeshProUGUI in inspector
    public TMP_Text shotsText;                // assign a TextMeshProUGUI in inspector

    [Header("Win UI / Fade")]
    public CanvasGroup fadeCanvasGroup;       // optional: full-screen panel with black Image and CanvasGroup
    public TMP_Text winText;                  // assign TMP for the win message
    public float fadeDuration = 1.0f;

    // Only one target at a time
    GameObject currentTarget;
    Camera mainCam;
    Coroutine monitorCoroutine;

    // Game tracking
    int targetsHit = 0;
    int totalShots = 0;
    bool gameWon = false;

    // Singleton convenience so player can notify shots
    public static targetManagerScript Instance { get; private set; }

    // track spawned pucks directly (avoids relying on tags)
    List<GameObject> activePucks = new List<GameObject>();

    // track only player-shot pucks (these are the ones that should count for success)
    List<GameObject> trackedShotPucks = new List<GameObject>();

    // make sure Instance is set in Awake
    void Awake()
    {
        // ensure UI references exist (creates Canvas, Fade panel, TMP texts if missing)
        EnsureUIExists();

        Instance = this;
        if (fadeCanvasGroup != null)
        {
            // start transparent
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
        }
    }

    // Creates basic UI at runtime when any required UI reference is null.
    void EnsureUIExists()
    {
        // if everything assigned, nothing to do
        if (targetsLeftText != null && shotsText != null && winText != null && fadeCanvasGroup != null)
            return;

        // find or create Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("UI_Canvas", typeof(RectTransform));
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // create fade panel (black full-screen Image + CanvasGroup)
        if (fadeCanvasGroup == null)
        {
            GameObject fadeGO = new GameObject("FadePanel", typeof(RectTransform));
            fadeGO.transform.SetParent(canvas.transform, false);
            var rt = fadeGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = fadeGO.AddComponent<Image>();
            img.color = Color.black; // full black, alpha controlled by CanvasGroup

            var cg = fadeGO.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            fadeCanvasGroup = cg;
        }

        // create win text (center)
        if (winText == null)
        {
            GameObject winGO = new GameObject("WinText", typeof(RectTransform));
            winGO.transform.SetParent(canvas.transform, false);
            var rt = winGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(900, 300);

            var tmp = winGO.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 42;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            tmp.gameObject.SetActive(false);
            winText = tmp;
        }

        // create targets-left text (top-left)
        if (targetsLeftText == null)
        {
            GameObject go = new GameObject("TargetsLeftText", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta = new Vector2(300, 40);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.fontSize = 24;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            targetsLeftText = tmp;
        }

        // create shots text (below targets-left)
        if (shotsText == null)
        {
            GameObject go = new GameObject("ShotsText", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -40f);
            rt.sizeDelta = new Vector2(300, 40);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.fontSize = 24;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            shotsText = tmp;
        }
    }

    void Start()
    {
        mainCam = Camera.main;
        UpdateUI();
        if (spawnOnStart)
            SpawnNewTarget();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // Use the new Input System: press R to force spawn a new target (only while playing)
        if (gameWon) return;

        if (Keyboard.current == null)
            return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
            SpawnNewTarget();
    }

    // Public: ask manager to spawn a new target (will remove existing)
    public void SpawnNewTarget()
    {
        if (gameWon) return; // don't spawn after win

        if (targetPrefab == null)
        {
            Debug.LogWarning("targetManagerScript: targetPrefab not assigned.");
            return;
        }

        // remove existing target if any
        if (currentTarget != null)
            Destroy(currentTarget);

        // clear any previously tracked pucks (they should be destroyed by game flow, but be safe)
        for (int i = activePucks.Count - 1; i >= 0; i--)
        {
            var p = activePucks[i];
            if (p != null) Destroy(p);
        }
        activePucks.Clear();

        // also clear any tracked shot pucks from previous round
        for (int i = trackedShotPucks.Count - 1; i >= 0; i--)
        {
            var p = trackedShotPucks[i];
            // do not destroy player-shot pucks here (they should be destroyed above via activePucks if appropriate),
            // but make sure list is empty so we don't get stale references.
            trackedShotPucks.RemoveAt(i);
        }

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

        //Ensure the target is below pucks
        spawnPos.z = -1f;
        currentTarget = Instantiate(targetPrefab, spawnPos, Quaternion.identity);
        // Ensure the target is purely visual:
        var rb = currentTarget.GetComponent<Rigidbody2D>();
        if (rb != null) Destroy(rb);
        var rb3 = currentTarget.GetComponent<Rigidbody>();
        if (rb3 != null) Destroy(rb3);

        // spawn pucks around the target (3..5) and keep them inside viewport margin if possible
        if (puckPrefab != null)
        {
            if (mainCam == null) mainCam = Camera.main;
            int count = Random.Range(minPucks, maxPucks + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 puckPos = Vector3.zero;
                bool placed = false;
                for (int attempt = 0; attempt < spawnRetryAttempts; attempt++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float dist = Random.Range(puckSpawnRadiusMin, puckSpawnRadiusMax);
                    puckPos = currentTarget.transform.position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * dist;
                    if (mainCam != null)
                    {
                        Vector3 vp = mainCam.WorldToViewportPoint(puckPos);
                        if (vp.z <= 0) continue; // behind camera
                        if (vp.x < viewportMargin || vp.x > 1f - viewportMargin || vp.y < viewportMargin || vp.y > 1f - viewportMargin)
                            continue;
                    }
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    // fallback: clamp to nearest valid viewport position around target
                    if (mainCam != null)
                    {
                        Vector3 vp = mainCam.WorldToViewportPoint(currentTarget.transform.position);
                        vp.x = Mathf.Clamp(vp.x, viewportMargin, 1f - viewportMargin);
                        vp.y = Mathf.Clamp(vp.y, viewportMargin, 1f - viewportMargin);
                        vp.z = Mathf.Abs(mainCam.transform.position.z);
                        puckPos = mainCam.ViewportToWorldPoint(vp);
                        puckPos.z = 0f;
                    }
                    else
                    {
                        puckPos = currentTarget.transform.position + (Vector3.right * puckSpawnRadiusMin * (i + 1));
                    }
                }

                var puck = Instantiate(puckPrefab, puckPos, Quaternion.identity);
                // track the spawned puck so MonitorPucks doesn't rely on tags
                activePucks.Add(puck);
            }
        }

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
        // Notes: only consider player-shot pucks (trackedShotPucks) for success.
        // This avoids initial stationary pucks around the target causing false positives.
        while (currentTarget != null)
        {
            // if no player-shot pucks yet, just wait
            if (trackedShotPucks.Count == 0)
            {
                yield return new WaitForSeconds(checkInterval);
                continue;
            }

            Vector2 targetPos = currentTarget.transform.position;

            // iterate backwards to allow removal of destroyed entries
            for (int i = trackedShotPucks.Count - 1; i >= 0; i--)
            {
                var p = trackedShotPucks[i];
                if (p == null)
                {
                    trackedShotPucks.RemoveAt(i);
                    continue;
                }

                float dist = Vector2.Distance(p.transform.position, targetPos);
                if (dist <= successRadius)
                {
                    // check if puck has (almost) stopped
                    Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
                    bool stopped = (rb == null) || (rb.linearVelocity.sqrMagnitude <= stopVelocityThreshold * stopVelocityThreshold);

                    if (stopped)
                    {
                        OnTargetHit();
                        yield break; // stop monitoring until new target is spawned (or win)
                    }
                }
            }

            yield return new WaitForSeconds(checkInterval);
        }
    }

    void OnTargetHit()
    {
        // increment target count
        targetsHit++;
        UpdateUI();

        // Clear all tracked pucks (visual + physics)
        for (int i = 0; i < activePucks.Count; i++)
        {
            if (activePucks[i] != null) Destroy(activePucks[i]);
        }
        activePucks.Clear();

        // Clear tracked player-shot pucks too
        for (int i = trackedShotPucks.Count - 1; i >= 0; i--)
        {
            if (trackedShotPucks[i] != null) Destroy(trackedShotPucks[i]);
        }
        trackedShotPucks.Clear();

        // remove current target
        if (currentTarget != null) Destroy(currentTarget);
        currentTarget = null;

        // if goal achieved -> win sequence
        if (targetsHit >= targetsToHit)
        {
            StartCoroutine(ShowWinSequence());
            return;
        }

        // otherwise spawn a new target after a short delay so player sees the result
        Invoke(nameof(SpawnNewTarget), 0.5f);
    }

    // Called by player when they shoot a puck
    public void RegisterShot()
    {
        totalShots++;
        UpdateUI();
    }

    // Called by player to let manager watch a newly-instantiated shot puck
    public void TrackPuck(GameObject puck)
    {
        if (puck == null) return;
        if (trackedShotPucks == null) trackedShotPucks = new System.Collections.Generic.List<GameObject>();
        trackedShotPucks.Add(puck);

        // also keep it in activePucks so it will be cleaned up with the rest
        if (activePucks == null) activePucks = new System.Collections.Generic.List<GameObject>();
        activePucks.Add(puck);
    }

    void UpdateUI()
    {
        if (targetsLeftText != null)
        {
            int left = Mathf.Max(targetsToHit - targetsHit, 0);
            targetsLeftText.text = $"Targets left: {left}";
        }

        if (shotsText != null)
        {
            shotsText.text = $"Shots: {totalShots}";
        }
    }

    IEnumerator ShowWinSequence()
    {
        // freeze game simulation (physics/time) but keep coroutines/input responsive by using unscaled time
        gameWon = true;
        Time.timeScale = 0f;

        // prepare win text
        if (winText != null)
        {
            winText.gameObject.SetActive(true);
            winText.text = $"You win!\nYou shot: {totalShots} times!\nPress any button to restart";
        }

        // ensure fade panel is visible and blocks input behind it
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.interactable = false;
        }

        // fade to black using unscaled time so it works while timeScale == 0
        if (fadeCanvasGroup != null)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeDuration));
                yield return null; // runs with unscaledDeltaTime updates
            }
            fadeCanvasGroup.alpha = 1f;
        }

        // wait for any input using the Input System (works while timeScale==0)
        bool anyPressed = false;
        while (!anyPressed)
        {
            // keyboard
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            {
                anyPressed = true;
                break;
            }

            // gamepad - check any button on the current device
            if (Gamepad.current != null)
            {
                foreach (var ctrl in Gamepad.current.allControls)
                {
                    if (ctrl is ButtonControl bc && bc.wasPressedThisFrame)
                    {
                        anyPressed = true;
                        break;
                    }
                }
                if (anyPressed) break;
            }

            // mouse buttons
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame)
                {
                    anyPressed = true;
                    break;
                }
            }

            // touch
            if (Touchscreen.current != null)
            {
                if (Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                {
                    anyPressed = true;
                    break;
                }
            }

            yield return null;
        }

        // restore time scale and restart
        Time.timeScale = 1f;
        Restart();
    }

    void Restart()
    {
        // reload active scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Optional: editor helper to force respawn
    [ContextMenu("ForceSpawnTarget")]
    void ForceSpawnTarget() => SpawnNewTarget();
}
