using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Efek partikel uGUI elektrik & pulse glow untuk tombol sambung kabel (Call-to-Action / Attention Grabber).
/// Menampilkan:
///  1. Gelombang ripple ring melingkar yang mengembang dan memudar (expanding glow wave).
///  2. Percikan partikel elektrik / bintang bercahaya (✦, •, ⚡) yang melayang keluar dari tombol.
///  3. Denyutan skala halus (breathing pulse) pada tombol agar user tertarik untuk mengkliknya.
/// </summary>
public class UIElectricParticleEffect : MonoBehaviour
{
    [Header("Particle Settings")]
    [SerializeField] private int particleCount = 10;
    [SerializeField] private float emitRadius = 38f;
    [SerializeField] private float particleSpeed = 35f;
    [SerializeField] private float minLife = 0.6f;
    [SerializeField] private float maxLife = 1.2f;

    [Header("Colors")]
    [SerializeField] private Color primaryColor = new Color(0.00f, 0.95f, 1.00f, 0.90f);  // Cyan neon
    [SerializeField] private Color secondaryColor = new Color(1.00f, 0.92f, 0.40f, 0.90f); // Kuning kilat
    [SerializeField] private Color ringColor = new Color(0.00f, 0.85f, 1.00f, 0.60f);

    [Header("Pulse Settings")]
    [SerializeField] private bool enableButtonPulse = true;
    [SerializeField] private float pulseSpeed = 4.5f;
    [SerializeField] private float pulseAmount = 0.07f;

    private class ParticleItem
    {
        public RectTransform rect;
        public TextMeshProUGUI tmp;
        public Vector2 velocity;
        public float life;
        public float maxLife;
        public float startSize;
        public Color baseColor;
    }

    private class RippleRing
    {
        public RectTransform rect;
        public Image img;
        public float timer;
        public float duration;
    }

    private readonly List<ParticleItem> _particles = new();
    private readonly List<RippleRing> _rings = new();
    private RectTransform _rootRt;
    private Vector3 _originalScale = Vector3.one;
    private Sprite _circleSprite;
    private TMP_FontAsset _fontAsset;

    private static readonly string[] SparkSymbols = { "✦", "★", "•", "⚡", "✧" };

    private void Awake()
    {
        _rootRt = GetComponent<RectTransform>();
        if (_rootRt != null)
            _originalScale = _rootRt.localScale;

        FindResources();
        CreateRipples();
        CreateParticles();
    }

    private void OnEnable()
    {
        if (_rootRt != null)
            _rootRt.localScale = _originalScale;

        // Reset all particles
        for (int i = 0; i < _particles.Count; i++)
        {
            ResetParticle(_particles[i]);
            _particles[i].life = Random.Range(0f, _particles[i].maxLife); // stagger
        }

        // Reset rings
        for (int i = 0; i < _rings.Count; i++)
        {
            _rings[i].timer = i * (_rings[i].duration / _rings.Count);
        }
    }

    private void OnDisable()
    {
        if (_rootRt != null)
            _rootRt.localScale = _originalScale;
    }

    private void FindResources()
    {
        if (SimsInventoryUI.Instance != null)
        {
            _circleSprite = SimsInventoryUI.Instance.circleButtonSprite;
            _fontAsset = SimsInventoryUI.Instance.fontAsset;
        }

        if (_circleSprite == null)
        {
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in sprites)
            {
                if (s.name == "Button Circle") { _circleSprite = s; break; }
            }
        }

        if (_fontAsset == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in fonts)
            {
                if (f.name.Contains("Varela")) { _fontAsset = f; break; }
            }
        }
    }

    private void CreateRipples()
    {
        // 2 concentric ripple rings behind button
        for (int i = 0; i < 2; i++)
        {
            GameObject ringGO = new GameObject($"RippleRing_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringGO.transform.SetParent(transform, false);
            ringGO.transform.SetAsFirstSibling(); // di belakang icon

            RectTransform rt = ringGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(emitRadius * 2f, emitRadius * 2f);

            Image img = ringGO.GetComponent<Image>();
            img.sprite = _circleSprite;
            img.type = Image.Type.Simple;
            img.color = new Color(ringColor.r, ringColor.g, ringColor.b, 0f);
            img.raycastTarget = false;

            _rings.Add(new RippleRing
            {
                rect = rt,
                img = img,
                timer = i * 0.75f,
                duration = 1.5f
            });
        }
    }

    private void CreateParticles()
    {
        for (int i = 0; i < particleCount; i++)
        {
            GameObject pGO = new GameObject($"Sparkle_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            pGO.transform.SetParent(transform, false);

            RectTransform rt = pGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24f, 24f);

            TextMeshProUGUI tmp = pGO.GetComponent<TextMeshProUGUI>();
            if (_fontAsset != null) tmp.font = _fontAsset;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;

            var p = new ParticleItem
            {
                rect = rt,
                tmp = tmp,
                maxLife = Random.Range(minLife, maxLife),
                startSize = Random.Range(14f, 20f),
                baseColor = (i % 3 == 0) ? secondaryColor : primaryColor
            };

            ResetParticle(p);
            p.life = Random.Range(0f, p.maxLife); // Stagger spawn
            _particles.Add(p);
        }
    }

    private void ResetParticle(ParticleItem p)
    {
        p.life = 0f;
        p.maxLife = Random.Range(minLife, maxLife);
        p.startSize = Random.Range(13f, 22f);
        p.baseColor = (Random.value > 0.4f) ? primaryColor : secondaryColor;

        // Pilih simbol acak
        p.tmp.text = SparkSymbols[Random.Range(0, SparkSymbols.Length)];
        p.tmp.fontSize = p.startSize;

        // Spawn di sekeliling lingkaran tombol
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float r = emitRadius * Random.Range(0.85f, 1.15f);
        Vector2 startPos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        p.rect.anchoredPosition = startPos;

        // Velocity mengarah keluar dengan sedikit variasi spiral
        Vector2 outward = startPos.normalized;
        Vector2 tangent = new Vector2(-outward.y, outward.x);
        p.velocity = (outward * Random.Range(particleSpeed * 0.7f, particleSpeed * 1.4f))
                   + (tangent * Random.Range(-12f, 12f));

        p.rect.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 1. Button breathing pulse
        if (enableButtonPulse && _rootRt != null)
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
            _rootRt.localScale = _originalScale * pulse;
        }

        // 2. Ripple rings update
        for (int i = 0; i < _rings.Count; i++)
        {
            var ring = _rings[i];
            ring.timer += dt;
            if (ring.timer >= ring.duration)
                ring.timer = 0f;

            float t = ring.timer / ring.duration; // 0..1
            // Scale dari 1.0 ke 1.65
            float scale = Mathf.Lerp(1.0f, 1.65f, t);
            ring.rect.localScale = new Vector3(scale, scale, 1f);

            // Alpha dari 0.7 meluruh ke 0
            float alpha = Mathf.Sin(t * Mathf.PI) * ringColor.a;
            ring.img.color = new Color(ringColor.r, ringColor.g, ringColor.b, alpha);
        }

        // 3. Sparkle particles update
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.life += dt;
            if (p.life >= p.maxLife)
            {
                ResetParticle(p);
                continue;
            }

            float progress = p.life / p.maxLife; // 0..1

            // Gerakkan posisi
            p.rect.anchoredPosition += p.velocity * dt;
            p.rect.localEulerAngles += new Vector3(0f, 0f, 45f * dt);

            // Alpha: fade in cepat, lalu fade out
            float alpha;
            if (progress < 0.2f)
                alpha = progress / 0.2f;
            else
                alpha = 1f - ((progress - 0.2f) / 0.8f);

            // Scale mengecil saat mendekati akhir
            float currentScale = Mathf.Lerp(1.2f, 0.4f, progress);
            p.rect.localScale = new Vector3(currentScale, currentScale, 1f);

            Color c = p.baseColor;
            p.tmp.color = new Color(c.r, c.g, c.b, alpha * c.a);
        }
    }
}
