using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class BumpingImpactVFX : MonoBehaviour
{
    [Header("Playback")]
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool hideRendererWhenFinished = true;
    [SerializeField, Min(0.01f)] private float duration = 0.45f;
    [SerializeField] private Vector2 impactCenterUV = new Vector2(0.5f, 0.5f);

    [Header("Shape")]
    [SerializeField, Range(0.05f, 1.5f)] private float maxRadius = 0.72f;
    [SerializeField, Range(0.005f, 0.25f)] private float ringWidth = 0.055f;
    [SerializeField, Range(0.0f, 3.0f)] private float bumpStrength = 1.15f;
    [SerializeField, Range(0.0f, 0.2f)] private float distortion = 0.035f;

    [Header("Breakup")]
    [SerializeField, Range(1.0f, 80.0f)] private float noiseScale = 23.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float noiseAmount = 0.28f;
    [SerializeField, Range(0.0f, 1.0f)] private float centerFlash = 0.55f;
    [SerializeField, Range(0.2f, 6.0f)] private float fadeOutPower = 1.45f;
    [SerializeField, Range(0.0f, 2.0f)] private float alpha = 1.0f;

    private static readonly int ImpactCenterId = Shader.PropertyToID("_ImpactCenter");
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");
    private static readonly int MaxRadiusId = Shader.PropertyToID("_MaxRadius");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int BumpStrengthId = Shader.PropertyToID("_BumpStrength");
    private static readonly int DistortionId = Shader.PropertyToID("_Distortion");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseAmountId = Shader.PropertyToID("_NoiseAmount");
    private static readonly int CenterFlashId = Shader.PropertyToID("_CenterFlash");
    private static readonly int FadeOutPowerId = Shader.PropertyToID("_FadeOutPower");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private Renderer cachedRenderer;
    private MaterialPropertyBlock propertyBlock;
    private float age;
    private bool playing;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
        ApplyProperties();
    }

    private void OnEnable()
    {
        if (playOnEnable)
        {
            Play(impactCenterUV);
        }
        else
        {
            SetRendererVisible(!hideRendererWhenFinished);
            ApplyProperties();
        }
    }

    private void Update()
    {
        if (!playing)
        {
            return;
        }

        age += Time.deltaTime;
        if (age >= duration)
        {
            age = duration;
            playing = false;
            SetRendererVisible(!hideRendererWhenFinished);
        }

        ApplyProperties();
    }

    public void Play()
    {
        Play(impactCenterUV);
    }

    public void Play(Vector2 uvCenter)
    {
        impactCenterUV = new Vector2(Mathf.Clamp01(uvCenter.x), Mathf.Clamp01(uvCenter.y));
        age = 0.0f;
        playing = true;
        SetRendererVisible(true);
        ApplyProperties();
    }

    public void PlayAtHit(RaycastHit hit)
    {
        Play(hit.textureCoord);
    }

    private void ApplyProperties()
    {
        if (cachedRenderer == null)
        {
            cachedRenderer = GetComponent<Renderer>();
        }

        propertyBlock ??= new MaterialPropertyBlock();

        cachedRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(ImpactCenterId, new Vector4(impactCenterUV.x, impactCenterUV.y, 0.0f, 0.0f));
        propertyBlock.SetFloat(AgeId, age);
        propertyBlock.SetFloat(DurationId, duration);
        propertyBlock.SetFloat(MaxRadiusId, maxRadius);
        propertyBlock.SetFloat(RingWidthId, ringWidth);
        propertyBlock.SetFloat(BumpStrengthId, bumpStrength);
        propertyBlock.SetFloat(DistortionId, distortion);
        propertyBlock.SetFloat(NoiseScaleId, noiseScale);
        propertyBlock.SetFloat(NoiseAmountId, noiseAmount);
        propertyBlock.SetFloat(CenterFlashId, centerFlash);
        propertyBlock.SetFloat(FadeOutPowerId, fadeOutPower);
        propertyBlock.SetFloat(AlphaId, alpha);
        cachedRenderer.SetPropertyBlock(propertyBlock);
    }

    private void SetRendererVisible(bool visible)
    {
        if (cachedRenderer == null)
        {
            cachedRenderer = GetComponent<Renderer>();
        }

        cachedRenderer.enabled = visible;
    }
}
