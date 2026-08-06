using TMPro;
using UI.Buttons;
using UI.Views.Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace SneakOut.PortalSettings;

internal static class PortalSettingsLayout
{
    public const string DummySectionName = "CodexPortalDummyBotSection";
    public const string ModeSectionName = "CodexPortalModeSection";
    public const string MapsSectionName = "CodexPortalMapsSection";

    private const float CollapsedBackgroundHeight = 643f;
    private const float ExpandedBackgroundHeight = 680f;
    private const float ExpandedBackgroundOffsetY = 0f;
    private const float StockRowWidth = 355.286f;
    private const float StockRowHeight = 85f;
    private const float CompactRowHeight = 85f;
    private const float DummyRowHeight = 122f;
    private const float MapsHeight = 105f;
    private const float NativeTitleY = 28.2f;
    private const float DummyTitleY = 45.2f;
    private const float ExpandedPreferredRoleY = 602.5f;
    private const float ExpandedPublicGameY = 514.5f;
    private const float ExpandedDummyBotY = 408f;
    private const float CollapsedPreferredRoleY = 565.5f;
    private const float CollapsedPublicGameY = 477.5f;
    private const float CollapsedDummyBotY = 389.5f;
    private const float ModeY = 301.5f;
    private const float MapsY = 203.5f;
    private const float PlayPanelY = 90.5f;
    private const float ExpandedExitButtonY = 325f;
    private const float CollapsedExitButtonY = 306.5f;

    private static Sprite? _roundedButtonSprite;
    private static Sprite? _roundedButtonOutlineSprite;

    public static NativePortalSection? CreateNativeSection(
        PortalPlayView view,
        string name,
        string title)
    {
        var settingsBackground = GetSettingsBackground(view);
        var template = settingsBackground is null
            ? null
            : FindDirectSection(view._privateGameButton?.transform, settingsBackground)
                ?? FindDirectSection(view._preferredRoleButton?.transform, settingsBackground);
        if (settingsBackground is null || template is null)
        {
            return null;
        }

        var root = UnityEngine.Object.Instantiate(template.gameObject, settingsBackground, false);
        root.name = name;
        root.SetActive(true);
        var titleLabel = root.transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();
        if (titleLabel is null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        titleLabel.text = title;
        titleLabel.raycastTarget = false;
        RemoveClonedTextBehaviours(titleLabel);
        for (var childIndex = 0; childIndex < root.transform.childCount; childIndex++)
        {
            var child = root.transform.GetChild(childIndex);
            if (child is not null && child.Pointer != titleLabel.transform.Pointer)
            {
                child.gameObject.SetActive(false);
            }
        }

        Apply(view);
        return new NativePortalSection(root, titleLabel);
    }

    public static NativePortalSwitch? CreateNativeSwitch(
        PortalPlayView view,
        Transform parent,
        string name,
        string leftText,
        string rightText,
        float fontSize = 11f,
        bool usePreferredRoleTemplate = false)
    {
        var sourceButton = usePreferredRoleTemplate
            ? view._preferredRoleButton ?? view._privateGameButton
            : view._privateGameButton ?? view._preferredRoleButton;
        var sourceRoot = sourceButton?.transform.parent?.GetComponent<RectTransform>();
        if (sourceRoot is null)
        {
            return null;
        }

        var root = UnityEngine.Object.Instantiate(sourceRoot.gameObject, parent, false);
        root.name = name;
        root.SetActive(true);
        var rect = root.GetComponent<RectTransform>();
        var button = root.GetComponentInChildren<SpookedOutlineButton>(true);
        var leftRoot = root.transform.Find("Public") ?? root.transform.Find("Victim");
        var rightRoot = root.transform.Find("Private") ?? root.transform.Find("Hunter");
        var leftBackground = leftRoot?.GetComponent<Image>();
        var rightBackground = rightRoot?.GetComponent<Image>();
        var leftLabel = leftRoot?.GetComponentInChildren<TMP_Text>(true);
        var rightLabel = rightRoot?.GetComponentInChildren<TMP_Text>(true);
        if (rect is null
            || button is null
            || leftRoot is null
            || rightRoot is null
            || leftBackground is null
            || rightBackground is null
            || leftLabel is null
            || rightLabel is null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        var buttonRect = button.GetComponent<RectTransform>();
        var centerBackground = button.GetComponent<Image>();
        var centerOutline = button.transform.Find("Outline")?.GetComponent<Image>();
        if (buttonRect is null || centerBackground is null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button._targetColorImage = centerBackground;
        button._targetOutlineImage = centerOutline;
        centerBackground.enabled = true;

        var raycastTarget = button.transform.Find("RaycastTarget")?.GetComponent<RectTransform>();
        var leftIcon = button.transform.Find(usePreferredRoleTemplate ? "VictimImage" : "PublicGame")?.gameObject;
        var rightIcon = button.transform.Find(usePreferredRoleTemplate ? "HunterImage" : "PrivateGame")?.gameObject;

        ConfigureSwitchHalf(leftRoot.GetComponent<RectTransform>(), leftBackground, leftLabel, true, leftText, fontSize);
        ConfigureSwitchHalf(rightRoot.GetComponent<RectTransform>(), rightBackground, rightLabel, false, rightText, fontSize);
        return new NativePortalSwitch(
            root,
            rect,
            button,
            buttonRect,
            raycastTarget,
            leftBackground,
            rightBackground,
            centerBackground,
            leftLabel,
            rightLabel,
            leftIcon,
            rightIcon,
            leftText,
            rightText);
    }

    public static void UseNativeSwitchIcons(
        NativePortalSwitch nativeSwitch,
        string leftSpriteName,
        string rightSpriteName)
    {
        SetIconSprite(nativeSwitch.LeftIcon, FindLoadedSprite(leftSpriteName));
        SetIconSprite(nativeSwitch.RightIcon, FindLoadedSprite(rightSpriteName));
    }

    public static void SetNativeSwitchIconSize(
        NativePortalSwitch nativeSwitch,
        bool left,
        float size)
    {
        var icon = left ? nativeSwitch.LeftIcon : nativeSwitch.RightIcon;
        var rect = icon?.GetComponent<RectTransform>();
        if (rect is not null)
        {
            rect.sizeDelta = new Vector2(size, size);
        }
    }

    public static PortalSegmentButton? CreateSegmentButton(
        Transform parent,
        string name,
        SpookedOutlineButton styleSource,
        float fontSize = 12f)
    {
        var root = UnityEngine.Object.Instantiate(styleSource.gameObject, parent, false);
        root.name = name;
        var rect = root.GetComponent<RectTransform>();
        var button = root.GetComponent<SpookedOutlineButton>();
        var label = root.transform.Find("Label")?.GetComponent<TMP_Text>()
            ?? root.GetComponentInChildren<TMP_Text>(true);
        if (rect is null || button is null || label is null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        button.onClick = new Button.ButtonClickedEvent();
        var background = root.transform.Find("ColorBackground")?.GetComponent<Image>();
        var outline = root.transform.Find("Outline")?.GetComponent<Image>();
        if (background is null || outline is null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        button._targetColorImage = background;
        button._targetOutlineImage = outline;
        button.targetGraphic = background;
        root.SetActive(true);
        background.enabled = true;
        background.sprite = GetRoundedButtonSprite();
        background.type = Image.Type.Sliced;
        background.preserveAspect = false;
        background.fillCenter = true;
        var shadow = root.transform.Find("Shadow");
        if (shadow is not null)
        {
            shadow.gameObject.SetActive(false);
        }
        outline.sprite = GetRoundedButtonOutlineSprite();
        outline.type = Image.Type.Sliced;
        outline.preserveAspect = false;
        outline.fillCenter = true;
        outline.color = Color.white;
        outline.raycastTarget = false;
        button.transition = Selectable.Transition.None;

        label.fontSize = fontSize;
        label.fontSizeMin = Mathf.Max(8f, fontSize - 3f);
        label.fontSizeMax = fontSize;
        label.enableAutoSizing = true;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enabled = true;
        RemoveClonedTextBehaviours(label);
        StretchToRoot(background.rectTransform, rect);
        StretchToRoot(outline.rectTransform, rect);
        StretchToRoot(label.rectTransform, rect);
        return new PortalSegmentButton(root, rect, button, background, label);
    }

    public static void LayoutNativeSwitch(
        NativePortalSwitch nativeSwitch,
        float x,
        float y,
        float width,
        float height)
    {
        var rect = nativeSwitch.Rect;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        var centerRect = nativeSwitch.CenterRect;
        centerRect.anchorMin = new Vector2(0.323f, 0f);
        centerRect.anchorMax = new Vector2(0.677f, 1f);
        centerRect.pivot = new Vector2(0.5f, 0.5f);
        centerRect.offsetMin = Vector2.zero;
        centerRect.offsetMax = Vector2.zero;
        centerRect.anchoredPosition = Vector2.zero;
        centerRect.localScale = Vector3.one;
        if (nativeSwitch.RaycastTarget is { } raycastTarget)
        {
            raycastTarget.anchorMin = new Vector2(0.5f, 0.5f);
            raycastTarget.anchorMax = new Vector2(0.5f, 0.5f);
            raycastTarget.pivot = new Vector2(0.5f, 0.5f);
            raycastTarget.sizeDelta = new Vector2(width, height);
            raycastTarget.anchoredPosition = Vector2.zero;
            raycastTarget.localScale = Vector3.one;
        }

        LayoutSwitchIcon(nativeSwitch.LeftIcon, left: true);
        LayoutSwitchIcon(nativeSwitch.RightIcon, left: false);
    }

    public static void SetNativeSwitchPresentation(
        NativePortalSwitch nativeSwitch,
        bool leftSelected,
        Color selectedColor,
        Color deselectedColor,
        bool interactable,
        bool dimmed = false)
    {
        nativeSwitch.Button.interactable = interactable;
        nativeSwitch.LeftBackground.color = leftSelected ? selectedColor : deselectedColor;
        nativeSwitch.RightBackground.color = leftSelected ? deselectedColor : selectedColor;
        nativeSwitch.CenterBackground.color = selectedColor;
        nativeSwitch.LeftLabel.text = nativeSwitch.LeftText;
        nativeSwitch.RightLabel.text = nativeSwitch.RightText;
        SetLabelAlpha(nativeSwitch.LeftLabel, dimmed || !interactable ? 0.45f : leftSelected ? 1f : 0f);
        SetLabelAlpha(nativeSwitch.RightLabel, dimmed || !interactable ? 0.45f : leftSelected ? 0f : 1f);
        if (nativeSwitch.LeftIcon is not null)
        {
            nativeSwitch.LeftIcon.SetActive(leftSelected);
        }
        if (nativeSwitch.RightIcon is not null)
        {
            nativeSwitch.RightIcon.SetActive(!leftSelected);
        }
    }

    public static void LayoutSegment(
        PortalSegmentButton segment,
        float x,
        float y,
        float width,
        float height)
    {
        var rect = segment.Rect;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    public static RectTransform? GetSettingsBackground(PortalPlayView view)
    {
        return view._playButton?.transform.parent?.parent?.GetComponent<RectTransform>();
    }

    public static void Apply(PortalPlayView view)
    {
        var settingsBackground = GetSettingsBackground(view);
        var playPanel = view._playButton?.transform.parent?.GetComponent<RectTransform>();
        if (settingsBackground is null || playPanel is null)
        {
            return;
        }

        var dummySection = settingsBackground.Find(DummySectionName);
        var roleSwitch = dummySection?.Find("LobbyTestBotRoleSwitch");
        var roleVisible = roleSwitch?.gameObject.activeSelf == true;

        var backgroundSize = settingsBackground.sizeDelta;
        backgroundSize.y = roleVisible ? ExpandedBackgroundHeight : CollapsedBackgroundHeight;
        settingsBackground.sizeDelta = backgroundSize;
        var backgroundPosition = settingsBackground.anchoredPosition;
        backgroundPosition.y = ExpandedBackgroundOffsetY;
        settingsBackground.anchoredPosition = backgroundPosition;

        LayoutRow(
            FindDirectSection(view._preferredRoleButton?.transform, settingsBackground),
            roleVisible ? ExpandedPreferredRoleY : CollapsedPreferredRoleY,
            StockRowHeight);
        LayoutRow(
            FindDirectSection(view._privateGameButton?.transform, settingsBackground),
            roleVisible ? ExpandedPublicGameY : CollapsedPublicGameY,
            StockRowHeight);
        LayoutRow(
            dummySection,
            roleVisible ? ExpandedDummyBotY : CollapsedDummyBotY,
            roleVisible ? DummyRowHeight : StockRowHeight,
            roleVisible ? DummyTitleY : NativeTitleY);
        LayoutRow(settingsBackground.Find(ModeSectionName), ModeY, CompactRowHeight, NativeTitleY);
        LayoutRow(settingsBackground.Find(MapsSectionName), MapsY, MapsHeight, NativeTitleY);

        var playPosition = playPanel.anchoredPosition;
        playPosition.y = PlayPanelY;
        playPanel.anchoredPosition = playPosition;

        var exitButton = settingsBackground.Find("ExitButton")?.GetComponent<RectTransform>();
        if (exitButton is not null)
        {
            var position = exitButton.anchoredPosition;
            position.y = roleVisible ? ExpandedExitButtonY : CollapsedExitButtonY;
            exitButton.anchoredPosition = position;
        }
    }

    private static RectTransform? FindDirectSection(Transform? control, RectTransform settingsBackground)
    {
        var current = control;
        while (current?.parent is { } parent && parent.Pointer != settingsBackground.Pointer)
        {
            current = parent;
        }

        return current?.parent?.Pointer == settingsBackground.Pointer
            ? current.GetComponent<RectTransform>()
            : null;
    }

    private static void LayoutRow(Transform? row, float y, float height, float? titleY = null)
    {
        var rect = row?.GetComponent<RectTransform>();
        if (rect is null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = new Vector2(StockRowWidth, height);
        rect.anchoredPosition = new Vector2(0f, y);
        if (titleY.HasValue)
        {
            var title = row?.Find("Text (TMP)")?.GetComponent<RectTransform>();
            if (title is not null)
            {
                var titlePosition = title.anchoredPosition;
                titlePosition.y = titleY.Value;
                title.anchoredPosition = titlePosition;
            }
        }
    }

    private static void ConfigureSwitchHalf(
        RectTransform? half,
        Image background,
        TMP_Text label,
        bool left,
        string text,
        float fontSize)
    {
        if (half is null)
        {
            return;
        }

        half.anchorMin = new Vector2(left ? 0f : 0.5f, 0f);
        half.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
        half.pivot = new Vector2(0.5f, 0.5f);
        half.offsetMin = Vector2.zero;
        half.offsetMax = Vector2.zero;
        half.anchoredPosition = Vector2.zero;
        half.localScale = Vector3.one;
        background.preserveAspect = false;
        background.fillCenter = true;
        label.text = text;
        label.fontSize = fontSize;
        label.fontSizeMin = Mathf.Max(8f, fontSize - 3f);
        label.fontSizeMax = fontSize;
        label.enableAutoSizing = true;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        RemoveClonedTextBehaviours(label);
        StretchToRoot(label.rectTransform, half);
        label.rectTransform.anchorMin = new Vector2(left ? 0f : 0.32f, 0f);
        label.rectTransform.anchorMax = new Vector2(left ? 0.68f : 1f, 1f);
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
    }

    private static void LayoutSwitchIcon(GameObject? icon, bool left)
    {
        var rect = icon?.GetComponent<RectTransform>();
        if (rect is null)
        {
            return;
        }

        var horizontalAnchor = left ? 0.2f : 0.82f;
        rect.anchorMin = new Vector2(horizontalAnchor, 0.5f);
        rect.anchorMax = new Vector2(horizontalAnchor, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(28f, 28f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Sprite? FindLoadedSprite(string name)
    {
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite is not null
                && sprite.Pointer != IntPtr.Zero
                && string.Equals(sprite.name, name, StringComparison.Ordinal))
            {
                return sprite;
            }
        }

        return null;
    }

    private static void SetIconSprite(GameObject? icon, Sprite? sprite)
    {
        var image = icon?.GetComponent<Image>();
        if (image is null || sprite is null)
        {
            return;
        }

        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
    }

    private static void RemoveClonedTextBehaviours(TMP_Text label)
    {
        foreach (var behaviour in label.gameObject.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not null && behaviour.Pointer != label.Pointer)
            {
                UnityEngine.Object.Destroy(behaviour);
            }
        }
    }

    private static void SetLabelAlpha(TMP_Text label, float alpha)
    {
        var color = label.color;
        color.a = alpha;
        label.color = color;
    }

    private static Sprite GetRoundedButtonSprite()
    {
        if (_roundedButtonSprite is not null && _roundedButtonSprite.Pointer != IntPtr.Zero)
        {
            return _roundedButtonSprite;
        }

        const int size = 32;
        const float radius = 16f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CodexPortalRoundedButtonTexture",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var cornerX = x < radius ? radius : x >= size - radius ? size - radius : x + 0.5f;
                var cornerY = y < radius ? radius : y >= size - radius ? size - radius : y + 0.5f;
                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cornerX, cornerY));
                var alpha = Mathf.Clamp01(radius + 0.5f - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        _roundedButtonSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        _roundedButtonSprite.name = "CodexPortalRoundedButtonSprite";
        _roundedButtonSprite.hideFlags = HideFlags.HideAndDontSave;
        return _roundedButtonSprite;
    }

    private static Sprite GetRoundedButtonOutlineSprite()
    {
        if (_roundedButtonOutlineSprite is not null
            && _roundedButtonOutlineSprite.Pointer != IntPtr.Zero)
        {
            return _roundedButtonOutlineSprite;
        }

        const int size = 32;
        const float outerRadius = 16f;
        const float innerRadius = 13f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CodexPortalRoundedButtonOutlineTexture",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        var center = new Vector2(size * 0.5f, size * 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                var outerAlpha = Mathf.Clamp01(outerRadius + 0.5f - distance);
                var innerAlpha = Mathf.Clamp01(innerRadius + 0.5f - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, outerAlpha - innerAlpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        _roundedButtonOutlineSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(outerRadius, outerRadius, outerRadius, outerRadius));
        _roundedButtonOutlineSprite.name = "CodexPortalRoundedButtonOutlineSprite";
        _roundedButtonOutlineSprite.hideFlags = HideFlags.HideAndDontSave;
        return _roundedButtonOutlineSprite;
    }

    private static void StretchToRoot(RectTransform? leaf, RectTransform root)
    {
        var current = leaf;
        while (current is not null && current.Pointer != root.Pointer)
        {
            current.anchorMin = Vector2.zero;
            current.anchorMax = Vector2.one;
            current.pivot = new Vector2(0.5f, 0.5f);
            current.offsetMin = Vector2.zero;
            current.offsetMax = Vector2.zero;
            current.anchoredPosition = Vector2.zero;
            current.localScale = Vector3.one;
            current = current.parent?.GetComponent<RectTransform>();
        }
    }
}

internal sealed record NativePortalSection(GameObject Root, TMP_Text Title);

internal sealed record NativePortalSwitch(
    GameObject Root,
    RectTransform Rect,
    SpookedOutlineButton Button,
    RectTransform CenterRect,
    RectTransform? RaycastTarget,
    Image LeftBackground,
    Image RightBackground,
    Image CenterBackground,
    TMP_Text LeftLabel,
    TMP_Text RightLabel,
    GameObject? LeftIcon,
    GameObject? RightIcon,
    string LeftText,
    string RightText)
{
    public bool IsAlive =>
        Root is not null
        && Root.Pointer != IntPtr.Zero
        && Rect is not null
        && Rect.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && CenterRect is not null
        && CenterRect.Pointer != IntPtr.Zero
        && LeftBackground is not null
        && LeftBackground.Pointer != IntPtr.Zero
        && RightBackground is not null
        && RightBackground.Pointer != IntPtr.Zero
        && CenterBackground is not null
        && CenterBackground.Pointer != IntPtr.Zero
        && LeftLabel is not null
        && LeftLabel.Pointer != IntPtr.Zero
        && RightLabel is not null
        && RightLabel.Pointer != IntPtr.Zero;
}

internal sealed record PortalSegmentButton(
    GameObject Root,
    RectTransform Rect,
    SpookedOutlineButton Button,
    Image Background,
    TMP_Text Label)
{
    public bool IsAlive =>
        Root is not null
        && Root.Pointer != IntPtr.Zero
        && Rect is not null
        && Rect.Pointer != IntPtr.Zero
        && Button is not null
        && Button.Pointer != IntPtr.Zero
        && Background is not null
        && Background.Pointer != IntPtr.Zero
        && Label is not null
        && Label.Pointer != IntPtr.Zero;
}
