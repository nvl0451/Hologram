using UnityEngine;

[RequireComponent(typeof(SkinnedMeshRenderer))]
public class HologramFader : MonoBehaviour
{
    private SkinnedMeshRenderer skinnedMeshRenderer;

    private bool isFading = false;
    private float fadeDuration;
    private float fadeTimer;
    private bool fadingIn;

    // Tint colors
    private Color step1Color = new Color32(0xB9, 0xDE, 0xDE, 255);
    private Color step2Color = new Color32(0x25, 0xD2, 0xD2, 255);
    private Color step3Color = new Color32(0x00, 0x00, 0x00, 255);

    void Awake()
    {
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        InitializeMaterial();
        FadeIn(2f);
    }

    private void InitializeMaterial()
    {
        Material mat = skinnedMeshRenderer.material;
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        mat.EnableKeyword("_EMISSION");
        mat.renderQueue = 3000; // Transparent queue
    }

    public void FadeIn(float duration)
    {
        fadeDuration = duration;
        fadeTimer = 0f;
        fadingIn = true;
        isFading = true;

        Debug.Log("[FadeIn] Started");
        SetMaterialAlpha(0f);
        SetTint(step1Color);
    }

    public void FadeOut(float duration)
    {
        fadeDuration = duration;
        fadeTimer = 0f;
        fadingIn = false;
        isFading = true;

        Debug.Log("[FadeOut] Started");
        SetMaterialAlpha(1f); // Start fully visible
        SetTint(step3Color);
    }

    void Update()
    {
        if (isFading)
        {
            fadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(fadeTimer / fadeDuration);
            float alpha;
            Color tint;

            if (fadingIn)
            {
                alpha = Mathf.Lerp(0f, 1f, t);
                tint = t < 0.5f
                    ? Color.Lerp(step1Color, step2Color, t * 2f)
                    : Color.Lerp(step2Color, step3Color, (t - 0.5f) * 2f);
            }
            else
            {
                alpha = Mathf.Lerp(1f, 0f, t);
                tint = t < 0.5f
                    ? Color.Lerp(step3Color, step2Color, t * 2f)
                    : Color.Lerp(step2Color, step1Color, (t - 0.5f) * 2f);
            }

            SetMaterialAlpha(alpha);
            SetTint(tint);

            if (t >= 1f)
            {
                isFading = false;
                Debug.Log("[Fade] Complete. Final Alpha: " + alpha);
            }
        }

        if (Input.GetKeyDown(KeyCode.I)) FadeIn(2f);
        if (Input.GetKeyDown(KeyCode.O)) FadeOut(2f);
    }

    private void SetMaterialAlpha(float alpha)
    {
        Material mat = skinnedMeshRenderer.material;
        if (mat.HasProperty("_Alpha"))
        {
            mat.SetFloat("_Alpha", alpha);
            Debug.Log($"[SetAlpha] _Alpha set to {alpha}");
        }
        else
        {
            Debug.LogWarning("[SetAlpha] Material has no _Alpha property!");
        }
    }

    private void SetTint(Color tint)
    {
        Material mat = skinnedMeshRenderer.material;
        if (mat.HasProperty("_HologramTint"))
        {
            mat.SetColor("_HologramTint", tint);
            Debug.Log($"[SetTint] Tint set to {tint}");
        }
        else
        {
            Debug.LogWarning("[SetTint] Material has no _HologramTint property!");
        }
    }
}