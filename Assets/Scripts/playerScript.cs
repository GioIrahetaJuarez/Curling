using UnityEngine;
using UnityEngine.InputSystem;
using TMPro; // changed from UnityEngine.UI

public class playerScript : MonoBehaviour
{
    enum InputState { Idle, Aiming, Scrubbing }

    public GameObject puckPrefab;
    public Transform spawnPoint;
    public float shootForce = 10f;             // base force
    public float maxExtraForce = 20f;          // extra force added by scrubbing (scrub fills 0..1)
    public float angularRandomness = 0f;       // optional spread in degrees

    // Aiming
    public float aimMinAngle = -45f;
    public float aimMaxAngle = 45f;
    public float aimSweepSpeed = 120f; // degrees per second
    public float aimLength = 2.5f;

    // Scrubbing
    public float scrubPerPress = 0.05f;    // amount added per space press
    public float scrubFinalizeDelay = 1.0f; // seconds after last press before shoot
    public float maxScrub = 1f;

    // scrub visual prefab (appears and moves perpendicular to aim)
    [Header("Scrub Prefab")]
    public GameObject scrubPrefab;                  // prefab to spawn on each scrub press
    public float scrubSpawnSpacing = 0.25f;         // spacing multiplier so later presses spawn further out
    public float scrubPrefabSpeed = 2.5f;           // base speed applied to spawned scrub prefabs
    public float scrubSpeedScaleByAmount = 2.0f;    // additional speed scaling based on scrubAmount

    // UI (OnGUI)
    public Vector2 scrubBarSize = new Vector2(200, 20);
    public Vector2 scrubBarOffset = new Vector2(-20, 20);

    // --- Customizable GUI colors ---
    [Header("Scrub Bar Colors")]
    public Color scrubBarBorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
    public Color scrubBarBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color scrubBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
    public Color scrubBarLabelColor = Color.white;

    [Header("Aim Indicator Colors")]
    public Color aimStartColor = new Color(1f, 0.8f, 0.2f, 1f);
    public Color aimEndColor = new Color(1f, 0.8f, 0.2f, 0f);
    // --- end colors ---

    // Contextual UI text (now uses TextMeshPro)
    [Header("Contextual Text")]
    public TMP_Text contextText;                     // assign a TMP Text from the scene (Canvas)
    public Vector2 contextOffset = new Vector2(0f, 50f); // offset in screen pixels from spawnPoint

    // --- Scrub audio (new) ---
    [Header("Scrub Audio")]
    public AudioClip scrubClip;                      // clip to play on each scrub press
    public AudioSource scrubAudioSource;             // optional: assign an AudioSource; otherwise created at runtime
    [Tooltip("Pitch multiplier applied per unit of distanceAlong when scrubbing")]
    public float scrubPitchMultiplier = 0.2f;
    [Range(0f,1f)]
    public float scrubVolume = 0.9f;

    // Fire audio (play when puck is launched)
    [Header("Fire Audio")]
    public AudioClip fireClip;               // clip played when puck is fired
    public AudioSource fireAudioSource;      // optional: assign to control pitch/3D settings
    [Range(0f,2.0f)]
    public float fireVolume = 1f;

    InputState state = InputState.Idle;

    // internal
    float currentAimAngle = 0f;
    int aimDirectionSign = 1;
    float scrubAmount = 0f;
    float lastScrubPressTime = -Mathf.Infinity;
    float lockedAimAngle = 0f; // locked when entering scrubbing
    int scrubPressCount = 0;   // counts number of presses during current scrubbing phase

    // Visuals
    LineRenderer aimLine;

    void Start()
    {
        currentAimAngle = (aimMinAngle + aimMaxAngle) * 0.5f;
        CreateAimIndicator();
        SetAimIndicatorActive(false);

        // ensure context text initial visibility
        UpdateContextText();

        // ensure a scrub audio source exists if a clip is assigned
        if (scrubClip != null && scrubAudioSource == null)
        {
            GameObject go = new GameObject("ScrubAudioSource");
            go.transform.SetParent(transform, false);
            scrubAudioSource = go.AddComponent<AudioSource>();
            scrubAudioSource.playOnAwake = false;
            scrubAudioSource.spatialBlend = 0f; // 2D
        }

        // if a fireClip is supplied but no AudioSource, create a simple 2D AudioSource attached to player
        if (fireClip != null && fireAudioSource == null)
        {
            GameObject go = new GameObject("FireAudioSource");
            go.transform.SetParent(transform, false);
            fireAudioSource = go.AddComponent<AudioSource>();
            fireAudioSource.playOnAwake = false;
            fireAudioSource.spatialBlend = 0f; // 2D by default; change if you want 3D
        }
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        bool spacePressed = Keyboard.current.spaceKey.wasPressedThisFrame;

        switch (state)
        {
            case InputState.Idle:
                if (spacePressed)
                {
                    EnterAiming();
                }
                break;

            case InputState.Aiming:
                UpdateAiming();
                if (spacePressed)
                {
                    // lock aim and enter scrub mode
                    EnterScrubbing();
                }
                break;

            case InputState.Scrubbing:
                if (spacePressed)
                {
                    // every press increases scrub amount and resets finalize timer
                    scrubAmount = Mathf.Clamp(scrubAmount + scrubPerPress, 0f, maxScrub);
                    lastScrubPressTime = Time.time;

                    // increment press count and spawn a scrub prefab that moves perpendicular to aim
                    scrubPressCount++;
                    SpawnScrubPrefab(scrubPressCount, scrubAmount);
                }

                // keep the (locked) aim indicator visible while scrubbing
                UpdateAimIndicator();

                // if player stopped pressing and timer elapsed, finalize and shoot
                if (Time.time - lastScrubPressTime >= scrubFinalizeDelay && lastScrubPressTime > -Mathf.Infinity)
                {
                    FinalizeAndShoot();
                }
                break;
        }

        // update contextual UI text and position each frame
        UpdateContextText();
    }

    void EnterAiming()
    {
        state = InputState.Aiming;
        currentAimAngle = (aimMinAngle + aimMaxAngle) * 0.5f;
        aimDirectionSign = 1;
        SetAimIndicatorActive(true);
    }

    void UpdateAiming()
    {
        // sweep angle back and forth
        float sweepDelta = aimSweepSpeed * Time.deltaTime * aimDirectionSign;
        currentAimAngle += sweepDelta;
        if (currentAimAngle > aimMaxAngle)
        {
            currentAimAngle = aimMaxAngle;
            aimDirectionSign = -1;
        }
        else if (currentAimAngle < aimMinAngle)
        {
            currentAimAngle = aimMinAngle;
            aimDirectionSign = 1;
        }

        // draw indicator while aiming
        UpdateAimIndicator();
    }

    void EnterScrubbing()
    {
        state = InputState.Scrubbing;
        // DO NOT hide the aim indicator — keep it visible until the puck is fired
        // lock the current aim angle so scrubbing uses a fixed direction
        lockedAimAngle = currentAimAngle;
        scrubAmount = 0f;
        lastScrubPressTime = -Mathf.Infinity; // hasn't pressed yet
        scrubPressCount = 0;
    }

    void FinalizeAndShoot()
    {
        ShootWithScrub(scrubAmount);

        // hide aim indicator after shot
        SetAimIndicatorActive(false);

        // reset
        scrubAmount = 0f;
        lastScrubPressTime = -Mathf.Infinity;
        state = InputState.Idle;
    }

    void ShootWithScrub(float scrub)
    {
        if (puckPrefab == null) return;

        Vector3 pos = (spawnPoint != null) ? spawnPoint.position : transform.position + transform.right * 1f;
        Quaternion rot = (spawnPoint != null) ? spawnPoint.rotation : transform.rotation;

        GameObject puck = Instantiate(puckPrefab, pos, rot);
        Rigidbody2D rb = puck.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = puck.AddComponent<Rigidbody2D>();

        // use the locked aim angle (set when entering scrubbing)
        float angleToUse = lockedAimAngle;
        Vector2 dir = Quaternion.Euler(0f, 0f, angleToUse) * (Vector2)transform.right;

        if (angularRandomness > 0f)
        {
            float angle = Random.Range(-angularRandomness, angularRandomness);
            dir = Quaternion.Euler(0f, 0f, angle) * dir;
        }

        float extra = Mathf.Clamp01(scrub) * maxExtraForce;
        float force = shootForce + extra;

        rb.AddForce(dir.normalized * force, ForceMode2D.Impulse);

        // play firing sound
        PlayFireSound(pos);

        // register shot and let manager track the puck
        if (targetManagerScript.Instance != null)
        {
            targetManagerScript.Instance.RegisterShot();
            targetManagerScript.Instance.TrackPuck(puck);
        }
        else
        {
            var mgr = FindObjectOfType<targetManagerScript>();
            if (mgr != null)
            {
                mgr.RegisterShot();
                mgr.TrackPuck(puck);
            }
        }
    }

    // new helper to play the fire audio
    void PlayFireSound(Vector3 worldPos)
    {
        if (fireClip == null) return;

        if (fireAudioSource != null)
        {
            fireAudioSource.pitch = 1f;
            fireAudioSource.PlayOneShot(fireClip, fireVolume);
        }
        else
        {
            // fallback: create a temporary 3D audio source at the puck/world position
            AudioSource.PlayClipAtPoint(fireClip, worldPos, fireVolume);
        }
    }

    [Header("Scrub Movement")]
    public float scrubMoveSpeed = 4.5f; // controllable base movement speed for scrub prefabs

    // spawn scrub prefab along the aim indicator line, further down the line each press
    void SpawnScrubPrefab(int pressIndex, float currentScrubAmount)
    {
        if (scrubPrefab == null) return;

        // origin is spawnPoint or player
        Vector3 origin = (spawnPoint != null) ? spawnPoint.position : transform.position;

        // direction along the (locked) aim
        Vector2 aimDir = Quaternion.Euler(0f, 0f, lockedAimAngle) * (Vector2)transform.right;

        // distance along the aim line increases with each press; clamp so we don't go past aimLength
        float distanceAlong = Mathf.Min(pressIndex * scrubSpawnSpacing, aimLength);

        // compute speed using controllable base speed and scaling by current scrub amount
        float speed = scrubMoveSpeed + scrubSpeedScaleByAmount * currentScrubAmount;

        // lifetime and the half-life point (we want the prefab to cross the aim line at mid-life)
        float lifeSeconds = 0.1f;
        float halfLife = lifeSeconds * 0.5f;

        // perpendicular unit (right-hand perp of aimDir)
        Vector2 perpUnit = new Vector2(-aimDir.y, aimDir.x).normalized;

        // alternate left/right: odd presses -> right, even presses -> left
        int side = (pressIndex % 2 == 0) ? -1 : 1;

        // spawn further away from the line so that after halfLife seconds (moving toward the line)
        // the prefab will be crossing the aim indicator line.
        // spawnDistance is the distance from the line at spawn time: speed * halfLife.
        // add a bit of extra spacing by pressIndex so later presses spawn slightly further out.
        float perpSpawnDistance = speed * halfLife + Mathf.Abs(pressIndex) * scrubSpawnSpacing * 0.5f;

        // spawn position: place along the aim line at distanceAlong, but offset perpendicular
        // so the prefab must travel toward the line. side determines left/right.
        Vector3 spawnPos = origin + (Vector3)(aimDir.normalized * distanceAlong) + (Vector3)(perpUnit * side * 0.25f);

        GameObject go = Instantiate(scrubPrefab, spawnPos, Quaternion.identity);

        // ensure a Rigidbody2D exists and make it dynamic so it moves under velocity
        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.angularVelocity = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // set velocity toward the aim line so it reaches the line at half its lifespan
        Vector2 velocityTowardLine = -perpUnit * side * speed;
        rb.linearVelocity = velocityTowardLine;

        // orient prefab to face its movement direction (2D)
        float angle = Mathf.Atan2(velocityTowardLine.y, velocityTowardLine.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // play scrub audio with pitch based on distanceAlong
        if (scrubClip != null && scrubAudioSource != null)
        {
            float pitch = 0.5f + (distanceAlong * scrubPitchMultiplier);
            pitch = Mathf.Clamp(pitch, 0.5f, 3f);
            scrubAudioSource.pitch = pitch;
            scrubAudioSource.PlayOneShot(scrubClip, scrubVolume);
            // do not forcibly reset pitch here — subsequent calls will overwrite as needed
        }

        // short-lived: delete itself after a brief time
        Destroy(go, lifeSeconds);
    }

    // Aim indicator management using LineRenderer
    void CreateAimIndicator()
    {
        if (aimLine != null) return;

        GameObject go = new GameObject("AimIndicator");
        go.transform.SetParent(transform, false);
        aimLine = go.AddComponent<LineRenderer>();
        aimLine.positionCount = 2;
        aimLine.useWorldSpace = true;
        aimLine.startWidth = 0.06f;
        aimLine.endWidth = 0.02f;
        aimLine.material = new Material(Shader.Find("Sprites/Default"));
        // use customizable colors
        aimLine.startColor = aimStartColor;
        aimLine.endColor = aimEndColor;
        aimLine.sortingOrder = 1000;
        aimLine.enabled = false;
    }

    void SetAimIndicatorActive(bool on)
    {
        if (aimLine != null)
            aimLine.enabled = on;
    }

    void UpdateAimIndicator()
    {
        if (aimLine == null || !aimLine.enabled) return;

        Vector3 origin = (spawnPoint != null) ? spawnPoint.position : transform.position;
        // when scrubbing we use lockedAimAngle, otherwise currentAimAngle
        float angle = (state == InputState.Scrubbing) ? lockedAimAngle : currentAimAngle;
        Vector2 dir = Quaternion.Euler(0f, 0f, angle) * (Vector2)transform.right;
        Vector3 end = origin + (Vector3)dir.normalized * aimLength;
        aimLine.SetPosition(0, origin);
        aimLine.SetPosition(1, end);
    }

    // new: update contextual UI text based on current state and position it near spawnPoint
    void UpdateContextText()
    {
        if (contextText == null)
            return;

        string msg = "";
        switch (state)
        {
            case InputState.Idle:
                msg = "Press space to begin aiming";
                break;
            case InputState.Aiming:
                msg = "Press space to lock in your angle";
                break;
            case InputState.Scrubbing:
                msg = "Repeatedly press space to scrub the ice. More scrubbing results in faster firing speed";
                break;
        }

        contextText.text = msg;

        // position the UI text near the spawn point / player with the given offset
        Camera cam = Camera.main;
        Vector3 worldPos = (spawnPoint != null) ? spawnPoint.position : transform.position;
        if (cam != null)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            // if behind camera, hide text
            if (screenPos.z <= 0f)
            {
                contextText.enabled = false;
            }
            else
            {
                contextText.enabled = true;
                // apply offset (x,y) in screen pixels
                Vector3 finalPos = screenPos + new Vector3(contextOffset.x, contextOffset.y, 0f);
                // place RectTransform position (works for Screen Space - Overlay canvases and Screen Space - Camera)
                contextText.rectTransform.position = finalPos;
            }
        }
        else
        {
            // fallback: just enable and do not move
            contextText.enabled = true;
        }
    }

    // Simple side scrub UI using OnGUI
    void OnGUI()
    {
        if (state != InputState.Scrubbing)
            return;

        // draw bar in top-right
        float x = Screen.width - scrubBarSize.x + scrubBarOffset.x;
        float y = scrubBarOffset.y;
        Rect bg = new Rect(x, y, scrubBarSize.x, scrubBarSize.y);

        // save GUI colors
        Color oldBG = GUI.backgroundColor;
        Color oldContent = GUI.contentColor;

        // draw border/background
        GUI.backgroundColor = scrubBarBorderColor;
        GUI.Box(bg, "");

        // inner background
        Rect innerBg = new Rect(x + 1, y + 1, scrubBarSize.x - 2, scrubBarSize.y - 2);
        GUI.backgroundColor = scrubBarBackgroundColor;
        GUI.Box(innerBg, "");

        // fill
        Rect fill = new Rect(x + 2, y + 2, (scrubBarSize.x - 4) * Mathf.Clamp01(scrubAmount / maxScrub), scrubBarSize.y - 4);
        GUI.backgroundColor = scrubBarFillColor;
        GUI.Box(fill, "");

        // label
        Rect label = new Rect(x - 110, y - 2, 100, scrubBarSize.y + 4);
        GUI.contentColor = scrubBarLabelColor;
        GUI.Label(label, "Scrub power");

        // restore GUI colors
        GUI.backgroundColor = oldBG;
        GUI.contentColor = oldContent;
    }
}
