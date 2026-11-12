using UnityEngine;
using UnityEngine.UI;

// Coloured dots drifting up behind the Color Hunter gameplay HUD (design mockup 2026-10).
// Spawned once under this RectTransform; they only move while the panel is active.
public class ChromaHuntFloatingDots : MonoBehaviour
{
    [SerializeField] private Sprite[] sprites;
    [SerializeField] private int count = 20;
    [SerializeField] private Vector2 sizeRange = new Vector2(24f, 56f);
    [SerializeField] private Vector2 speedRange = new Vector2(20f, 60f);
    [SerializeField] private float swayAmount = 18f;

    RectTransform[] _dots;
    float[] _speed, _phase, _x;
    float _width, _height;

    void Awake()
    {
        if (sprites == null || sprites.Length == 0 || count <= 0) return;

        var area = (RectTransform)transform;
        _width = area.rect.width;
        _height = area.rect.height;
        _dots = new RectTransform[count];
        _speed = new float[count];
        _phase = new float[count];
        _x = new float[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(area, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float size = Random.Range(sizeRange.x, sizeRange.y);
            rt.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            img.sprite = sprites[i % sprites.Length];
            img.raycastTarget = false;

            _dots[i] = rt;
            _speed[i] = Random.Range(speedRange.x, speedRange.y);
            _phase[i] = Random.Range(0f, Mathf.PI * 2f);
            _x[i] = Random.Range(-_width * 0.5f, _width * 0.5f);
            rt.anchoredPosition = new Vector2(_x[i], Random.Range(-_height * 0.5f, _height * 0.5f));
        }
    }

    void Update()
    {
        if (_dots == null) return;
        float top = _height * 0.5f + 40f;
        for (int i = 0; i < _dots.Length; i++)
        {
            float y = _dots[i].anchoredPosition.y + _speed[i] * Time.deltaTime;
            if (y > top)
            {
                y = -top;
                _x[i] = Random.Range(-_width * 0.5f, _width * 0.5f);
            }
            float sway = Mathf.Sin(Time.time * 0.8f + _phase[i]) * swayAmount;
            _dots[i].anchoredPosition = new Vector2(_x[i] + sway, y);
        }
    }
}
