#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Zombies55.EditorTools
{
    // Builds the whole UI hierarchy in the open scene and wires GameShell's serialized fields.
    public static class ShellBuilder
    {
        const string UiFolder = "Assets/Art/UI";
        const string RoundedRectPath = UiFolder + "/RoundedRect.png";
        const string VignettePath = UiFolder + "/Vignette.png";
        const string OutlineMaterialPath = UiFolder + "/UIText_Outline.mat";

        static Sprite s_RoundedRect;
        static Sprite s_Vignette;
        static Material s_Outline;

        // ------------------------------------------------------------------ public contract

        public static GameShell Build()
        {
            // Resources first, so a missing TMP import fails before we touch the scene.
            s_Outline = GetOrCreateOutlineMaterial();
            s_RoundedRect = GetOrCreateRoundedRect();
            s_Vignette = GetOrCreateVignette();

            var existing = UnityEngine.Object.FindAnyObjectByType<GameShell>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var canvasGo = new GameObject("UI", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create UI");

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var shell = canvasGo.AddComponent<GameShell>();
            var root = (RectTransform)canvasGo.transform;

            var title = BuildTitle(root, out var promptText);
            var hud = BuildHud(root, out var survivalHud);
            var pause = BuildPause(root, out var resume, out var pauseRestart, out var pauseTitle,
                out var musicSlider, out var sfxSlider);
            var end = BuildEnded(root, out var bannerText, out var bannerRect, out var statsText,
                out var extraText, out var endRestart, out var endTitle);

            // Overlays go last so they draw above everything.
            var flash = CreateOverlay(root, "FlashOverlay", Palette.WithAlpha(Palette.Accent, 0f));
            var fade = CreateOverlay(root, "FadeOverlay", Palette.WithAlpha(Palette.UiDark, 0f));

            var so = new SerializedObject(shell);
            Wire(so, "titleRoot", title);
            Wire(so, "promptText", promptText);
            Wire(so, "hudRoot", hud);
            Wire(so, "survivalHud", survivalHud);
            Wire(so, "pauseRoot", pause);
            Wire(so, "resumeButton", resume);
            Wire(so, "pauseRestartButton", pauseRestart);
            Wire(so, "pauseTitleButton", pauseTitle);
            Wire(so, "musicSlider", musicSlider);
            Wire(so, "sfxSlider", sfxSlider);
            Wire(so, "endRoot", end);
            Wire(so, "bannerText", bannerText);
            Wire(so, "bannerRect", bannerRect);
            Wire(so, "statsText", statsText);
            Wire(so, "extraStatsText", extraText);
            Wire(so, "endRestartButton", endRestart);
            Wire(so, "endTitleButton", endTitle);
            Wire(so, "fadeOverlay", fade);
            Wire(so, "flashOverlay", flash);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Start with only the title visible; GameShell fixes the rest at runtime.
            pause.SetActive(false);
            end.SetActive(false);
            hud.SetActive(false);
            extraText.gameObject.SetActive(false);

            EnsureEventSystem();
            return shell;
        }

        public static Material GetOrCreateOutlineMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (existing != null) return existing;

            var font = TMP_Settings.defaultFontAsset;
            if (font == null)
                throw new InvalidOperationException(
                    "TMP_Settings.defaultFontAsset is null. Import TMP Essential Resources first " +
                    "(Window > TextMeshPro > Import TMP Essential Resources).");

            EnsureFolder(UiFolder);

            var mat = new Material(font.material) { name = "UIText_Outline" };
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_OutlineWidth", 0.18f);
            mat.SetColor("_OutlineColor", Palette.UiDark);

            AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ------------------------------------------------------------------ screens

        static GameObject BuildTitle(RectTransform parent, out TMP_Text prompt)
        {
            var root = CreateStretched("TitleScreen", parent);

            var shadow = CreateText(root, "TitleShadow", "ZOMBIES 5.5", 165, Palette.WithAlpha(Palette.UiDark, 0.6f), 10f, true);
            Place(shadow.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(8f, -8f), new Vector2(1500f, 260f));

            var title = CreateText(root, "Title", "ZOMBIES 5.5", 165, Palette.Accent, 10f, true);
            Place(title.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1500f, 260f));

            var subtitle = CreateText(root, "Subtitle", "SURVIVE THE CITY", 56, Palette.UiLight, 24f, true);
            Place(subtitle.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(0f, -190f), new Vector2(1200f, 80f));

            prompt = CreateText(root, "Prompt", "PRESS ANY KEY", 40, Palette.UiLight, 14f, true);
            Place(prompt.rectTransform, new Vector2(0.5f, 0.25f), Vector2.zero, new Vector2(900f, 70f));

            var controls = CreateText(root, "Controls",
                "WASD MOVE · SHIFT SPRINT · SPACE JUMP · LMB FIRE · RMB AIM · R RELOAD · 1/2 SWAP · E BUY · ESC PAUSE",
                24, Palette.WithAlpha(Palette.UiLight, 0.85f), 2f, true);
            Place(controls.rectTransform, new Vector2(0.5f, 0.18f), Vector2.zero, new Vector2(1850f, 50f));

            return root.gameObject;
        }

        static GameObject BuildHud(RectTransform parent, out SurvivalHud hud)
        {
            var root = CreateStretched("Hud", parent);
            hud = root.gameObject.AddComponent<SurvivalHud>();

            Color panelColor = Palette.WithAlpha(Palette.UiDark, 0.8f);
            Color soft = Palette.WithAlpha(Palette.UiLight, 0.85f);

            // Damage vignette sits behind everything else.
            var vignette = CreateImage(root, "DamageVignette", s_Vignette, Palette.WithAlpha(Palette.Danger, 0f), false);
            Stretch(vignette.rectTransform);

            // Round counter, top-left.
            var roundLabel = CreateText(root, "RoundLabel", "ROUND", 30, soft, 12f, true);
            roundLabel.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(roundLabel.rectTransform, new Vector2(0f, 1f), new Vector2(56f, -34f), new Vector2(300f, 40f));

            var roundNumber = CreateText(root, "RoundNumber", "0", 150, Palette.Accent, 0f, true);
            roundNumber.alignment = TextAlignmentOptions.MidlineLeft;
            Place(roundNumber.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -151f), new Vector2(320f, 170f));
            roundNumber.rectTransform.pivot = new Vector2(0f, 0.5f); // pops from the left edge

            var enemies = CreateText(root, "EnemiesLeft", "0 ENEMIES LEFT", 34, Palette.UiLight, 4f, true);
            enemies.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(enemies.rectTransform, new Vector2(0f, 1f), new Vector2(56f, -240f), new Vector2(520f, 44f));

            // Health pips, bottom-left.
            var healthPanel = CreateImage(root, "HealthPanel", s_RoundedRect, panelColor, true);
            PlaceCorner(healthPanel.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(330f, 84f));

            var healthLabel = CreateText(healthPanel.rectTransform, "HealthLabel", "HEALTH", 22, soft, 8f, false);
            healthLabel.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(healthLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(200f, 28f));

            var pips = new UnityEngine.UI.Image[5];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = CreateImage(healthPanel.rectTransform, "Pip" + (i + 1), s_RoundedRect, Palette.UiLight, true);
                pips[i].pixelsPerUnitMultiplier = 2f;
                PlaceCorner(pips[i].rectTransform, new Vector2(0f, 0f), new Vector2(20f + i * 60f, 16f), new Vector2(52f, 24f));
            }

            // Points, top-right.
            var pointsLabel = CreateText(root, "PointsLabel", "POINTS", 28, soft, 12f, true);
            pointsLabel.alignment = TextAlignmentOptions.MidlineRight;
            PlaceCorner(pointsLabel.rectTransform, new Vector2(1f, 1f), new Vector2(-56f, -34f), new Vector2(300f, 36f));

            var points = CreateText(root, "Points", "0", 76, Palette.UiLight, 2f, true);
            points.alignment = TextAlignmentOptions.MidlineRight;
            Place(points.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -117f), new Vector2(500f, 90f));
            points.rectTransform.pivot = new Vector2(1f, 0.5f);

            var floatRoot = CreateStretched("PointFloats", root);
            var floatTemplate = CreateText(floatRoot, "FloatTemplate", "+0", 44, Palette.Accent, 2f, true);
            floatTemplate.alignment = TextAlignmentOptions.MidlineRight;
            Place(floatTemplate.rectTransform, new Vector2(1f, 1f), new Vector2(-56f, -205f), new Vector2(300f, 50f));
            floatTemplate.rectTransform.pivot = new Vector2(1f, 0.5f);
            floatTemplate.gameObject.SetActive(false);

            // Weapon panel, bottom-right.
            var weaponPanel = CreateImage(root, "WeaponPanel", s_RoundedRect, panelColor, true);
            PlaceCorner(weaponPanel.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(520f, 230f));
            var wp = weaponPanel.rectTransform;
            var top = new Vector2(0f, 1f);

            var weaponName = CreateText(wp, "WeaponName", "P-9 SIDEARM", 30, Palette.UiLight, 6f, true);
            weaponName.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(weaponName.rectTransform, top, new Vector2(24f, -14f), new Vector2(472f, 40f));

            var mag = CreateText(wp, "Magazine", "12", 100, Palette.UiLight, 0f, true);
            mag.alignment = TextAlignmentOptions.MidlineRight;
            PlaceCorner(mag.rectTransform, top, new Vector2(24f, -54f), new Vector2(250f, 104f));

            var reserve = CreateText(wp, "Reserve", "/ 84", 52, soft, 2f, true);
            reserve.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(reserve.rectTransform, top, new Vector2(288f, -76f), new Vector2(200f, 60f));

            var reloadBack = CreateImage(wp, "ReloadBar", s_RoundedRect, Palette.Shadow, true);
            reloadBack.pixelsPerUnitMultiplier = 2f;
            PlaceCorner(reloadBack.rectTransform, top, new Vector2(24f, -160f), new Vector2(472f, 12f));
            var reloadFill = CreateImage(reloadBack.rectTransform, "ReloadFill", s_RoundedRect, Palette.Accent, false);
            reloadFill.type = UnityEngine.UI.Image.Type.Filled; // a Filled image needs a sprite
            reloadFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            reloadFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            reloadFill.fillAmount = 0f;
            Stretch(reloadFill.rectTransform);
            reloadFill.rectTransform.offsetMin = new Vector2(2f, 2f);
            reloadFill.rectTransform.offsetMax = new Vector2(-2f, -2f);

            var chips = new UnityEngine.UI.Image[PlayerWeapons.SlotCount];
            var chipLabels = new TMP_Text[PlayerWeapons.SlotCount];
            for (int i = 0; i < chips.Length; i++)
            {
                chips[i] = CreateImage(wp, "SlotChip" + (i + 1), s_RoundedRect, Palette.WithAlpha(Palette.UiDark, 0.8f), true);
                chips[i].pixelsPerUnitMultiplier = 2f;
                PlaceCorner(chips[i].rectTransform, top, new Vector2(24f + i * 244f, -178f), new Vector2(228f, 40f));

                var label = CreateText(chips[i].rectTransform, "Label", (i + 1) + "  EMPTY", 22, Palette.UiLight, 2f, false);
                label.enableAutoSizing = true;
                label.fontSizeMin = 14f;
                label.fontSizeMax = 22f;
                Stretch(label.rectTransform);
                chipLabels[i] = label;
            }

            // Crosshair: four ticks around the center; SurvivalHud sets their positions every frame.
            var crosshair = NewRect("Crosshair", root);
            Place(crosshair, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var ticks = new RectTransform[4];
            string[] tickNames = { "TickUp", "TickDown", "TickLeft", "TickRight" };
            for (int i = 0; i < 4; i++)
            {
                var tick = CreateImage(crosshair, tickNames[i], null, Palette.UiLight, false);
                bool vertical = i < 2;
                Place(tick.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    vertical ? new Vector2(4f, 16f) : new Vector2(16f, 4f));
                var outline = tick.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = Palette.WithAlpha(Palette.UiDark, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                ticks[i] = tick.rectTransform;
            }

            // Hit marker: four short diagonal ticks that flash and fade.
            var hitRect = NewRect("HitMarker", crosshair);
            Place(hitRect, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var hitGroup = hitRect.gameObject.AddComponent<CanvasGroup>();
            hitGroup.alpha = 0f;
            hitGroup.interactable = false;
            hitGroup.blocksRaycasts = false;
            var hitImages = new UnityEngine.UI.Image[4];
            Vector2[] hitPos = { new Vector2(14f, 14f), new Vector2(-14f, 14f), new Vector2(-14f, -14f), new Vector2(14f, -14f) };
            for (int i = 0; i < 4; i++)
            {
                hitImages[i] = CreateImage(hitRect, "Hit" + (i + 1), null, Palette.UiLight, false);
                Place(hitImages[i].rectTransform, new Vector2(0.5f, 0.5f), hitPos[i], new Vector2(4f, 16f));
                hitImages[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -45f : 45f);
            }

            // Context prompt, lower third.
            var prompt = CreateText(root, "Prompt", string.Empty, 40, Palette.UiLight, 4f, true);
            Place(prompt.rectTransform, new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(1400f, 60f));

            // Round banner last so it draws above the HUD.
            var banner = NewRect("RoundBanner", root);
            Place(banner, new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(1700f, 230f));
            var bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.interactable = false;
            bannerGroup.blocksRaycasts = false;
            var bannerText = CreateText(banner, "BannerText", "ROUND 1", 130, Palette.Accent, 8f, true);
            Stretch(bannerText.rectTransform);
            var bannerSub = CreateText(banner, "BannerSub", string.Empty, 60, Palette.UiLight, 8f, true);
            Place(bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -125f), new Vector2(1200f, 80f));

            var so = new SerializedObject(hud);
            Wire(so, "roundNumberText", roundNumber);
            Wire(so, "roundNumberRect", roundNumber.rectTransform);
            Wire(so, "enemiesText", enemies);
            WireArray(so, "healthPips", pips);
            Wire(so, "vignette", vignette);
            Wire(so, "pointsText", points);
            Wire(so, "pointsRect", points.rectTransform);
            Wire(so, "floatTemplate", floatTemplate);
            Wire(so, "floatRoot", floatRoot);
            Wire(so, "weaponNameText", weaponName);
            Wire(so, "magText", mag);
            Wire(so, "reserveText", reserve);
            WireArray(so, "slotChips", chips);
            WireArray(so, "slotLabels", chipLabels);
            Wire(so, "reloadBar", reloadBack.gameObject);
            Wire(so, "reloadFill", reloadFill);
            Wire(so, "crosshairRoot", crosshair.gameObject);
            WireArray(so, "crosshairTicks", ticks);
            Wire(so, "hitMarkerGroup", hitGroup);
            Wire(so, "hitMarkerRect", hitRect);
            WireArray(so, "hitMarkerImages", hitImages);
            Wire(so, "promptText", prompt);
            Wire(so, "bannerRect", banner);
            Wire(so, "bannerGroup", bannerGroup);
            Wire(so, "bannerText", bannerText);
            Wire(so, "bannerSubText", bannerSub);
            so.ApplyModifiedPropertiesWithoutUndo();

            banner.gameObject.SetActive(false);
            return root.gameObject;
        }

        static GameObject BuildPause(RectTransform parent, out UnityEngine.UI.Button resume,
            out UnityEngine.UI.Button restart, out UnityEngine.UI.Button toTitle,
            out UnityEngine.UI.Slider music, out UnityEngine.UI.Slider sfx)
        {
            var root = CreateStretched("PauseScreen", parent);

            var scrim = CreateImage(root, "Scrim", null, Palette.WithAlpha(Palette.UiDark, 0.7f), true);
            scrim.raycastTarget = true; // blocks clicks behind the menu
            Stretch(scrim.rectTransform);

            var card = CreateImage(root, "Card", s_RoundedRect, Palette.WithAlpha(Palette.UiDark, 0.8f), true);
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 900f));

            var heading = CreateText(card.rectTransform, "Heading", "PAUSED", 110, Palette.Accent, 8f, true);
            Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 350f), new Vector2(700f, 140f));

            resume = CreateButton(card.rectTransform, "ResumeButton", "RESUME", new Vector2(0f, 200f));
            restart = CreateButton(card.rectTransform, "RestartButton", "RESTART", new Vector2(0f, 90f));
            toTitle = CreateButton(card.rectTransform, "TitleButton", "TITLE", new Vector2(0f, -20f));

            var musicLabel = CreateText(card.rectTransform, "MusicLabel", "MUSIC", 34, Palette.UiLight, 6f, true);
            Place(musicLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(600f, 50f));
            music = CreateSlider(card.rectTransform, "MusicSlider", new Vector2(0f, -195f));

            var sfxLabel = CreateText(card.rectTransform, "SfxLabel", "SFX", 34, Palette.UiLight, 6f, true);
            Place(sfxLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -285f), new Vector2(600f, 50f));
            sfx = CreateSlider(card.rectTransform, "SfxSlider", new Vector2(0f, -340f));

            return root.gameObject;
        }

        static GameObject BuildEnded(RectTransform parent, out TMP_Text banner, out RectTransform bannerRect,
            out TMP_Text stats, out TMP_Text extra, out UnityEngine.UI.Button restart, out UnityEngine.UI.Button toTitle)
        {
            var root = CreateStretched("EndScreen", parent);

            var scrim = CreateImage(root, "Scrim", null, Palette.WithAlpha(Palette.UiDark, 0.55f), true);
            Stretch(scrim.rectTransform);

            banner = CreateText(root, "Banner", "YOU DIDN'T MAKE IT", 130, Palette.Danger, 8f, true);
            bannerRect = banner.rectTransform;
            Place(bannerRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(1700f, 230f));

            stats = CreateText(root, "Stats", "ROUNDS SURVIVED  0", 46, Palette.UiLight, 4f, true);
            stats.lineSpacing = 10f;
            Place(stats.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(1400f, 260f));

            extra = CreateText(root, "ExtraStats", string.Empty, 36, Palette.Accent, 4f, true);
            Place(extra.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1400f, 50f));

            restart = CreateButton(root, "RestartButton", "RESTART", new Vector2(-230f, -230f), new Vector2(400f, 100f));
            toTitle = CreateButton(root, "TitleButton", "TITLE", new Vector2(230f, -230f), new Vector2(400f, 100f));

            var hint = CreateText(root, "RestartHint", "R / ENTER TO RESTART", 28, Palette.WithAlpha(Palette.UiLight, 0.7f), 6f, true);
            Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -320f), new Vector2(900f, 40f));

            return root.gameObject;
        }

        // ------------------------------------------------------------------ widgets

        static UnityEngine.UI.Button CreateButton(RectTransform parent, string name, string label,
            Vector2 pos, Vector2? size = null)
        {
            var image = CreateImage(parent, name, s_RoundedRect, default, true);
            image.raycastTarget = true;
            // Image.color stays at Unity's default white: the ColorBlock below does the tinting.
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), pos, size ?? new Vector2(440f, 90f));

            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = Palette.CharacterPrimary;
            colors.highlightedColor = Palette.Accent;
            colors.selectedColor = Palette.Accent;
            colors.pressedColor = Color.Lerp(Palette.Accent, Palette.UiDark, 0.25f);
            colors.disabledColor = Palette.WithAlpha(Palette.CharacterPrimary, 0.4f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            // Dark label on a light button: keeps the default material (no outline).
            var text = CreateText(image.rectTransform, "Label", label, 44, Palette.UiDark, 4f, false);
            Stretch(text.rectTransform);
            return button;
        }

        static UnityEngine.UI.Slider CreateSlider(RectTransform parent, string name, Vector2 pos)
        {
            var rt = NewRect(name, parent); // the slider root only needs a RectTransform
            Place(rt, new Vector2(0.5f, 0.5f), pos, new Vector2(560f, 44f));

            var bg = CreateImage(rt, "Background", s_RoundedRect, Palette.UiDark, true);
            bg.pixelsPerUnitMultiplier = 2f;
            bg.raycastTarget = true;
            SetAnchors(bg.rectTransform, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f));
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = NewRect("Fill Area", rt);
            SetAnchors(fillArea, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f));
            fillArea.offsetMin = new Vector2(6f, 0f);
            fillArea.offsetMax = new Vector2(-6f, 0f);

            var fill = CreateImage(fillArea, "Fill", s_RoundedRect, Palette.Accent, true);
            fill.pixelsPerUnitMultiplier = 2f;
            SetAnchors(fill.rectTransform, Vector2.zero, Vector2.one);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = NewRect("Handle Slide Area", rt);
            SetAnchors(handleArea, Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(18f, 0f);
            handleArea.offsetMax = new Vector2(-18f, 0f);

            var handle = CreateImage(handleArea, "Handle", s_RoundedRect, Palette.UiLight, true);
            handle.pixelsPerUnitMultiplier = 2f;
            handle.raycastTarget = true;
            SetAnchors(handle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(36f, 0f);
            handle.rectTransform.anchoredPosition = Vector2.zero;

            var slider = rt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = 0.5f;

            slider.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = slider.colors;
            colors.normalColor = Palette.UiLight;
            colors.highlightedColor = Palette.Accent;
            colors.selectedColor = Palette.Accent;
            colors.pressedColor = Color.Lerp(Palette.Accent, Palette.UiDark, 0.25f);
            colors.fadeDuration = 0.1f;
            slider.colors = colors;
            return slider;
        }

        // ------------------------------------------------------------------ primitives

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform CreateStretched(string name, Transform parent)
        {
            var rt = NewRect(name, parent);
            Stretch(rt);
            return rt;
        }

        // Image with the given sprite. A default (zero) color keeps Unity's default white.
        static UnityEngine.UI.Image CreateImage(Transform parent, string name, Sprite sprite, Color color, bool sliced)
        {
            var rt = NewRect(name, parent);
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            if (color != default) image.color = color;
            image.type = sliced && sprite != null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        static UnityEngine.UI.Image CreateOverlay(RectTransform parent, string name, Color color)
        {
            var image = CreateImage(parent, name, null, color, false);
            Stretch(image.rectTransform);
            return image;
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size,
            Color color, float spacing, bool outlined)
        {
            var rt = NewRect(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            if (outlined) tmp.fontSharedMaterial = s_Outline;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = spacing;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
        }

        // Point anchor (same min and max) with the pivot at the anchor, then position and size.
        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, bool keepPivot = false)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            if (!keepPivot) rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        // Same as Place, but the pivot matches the anchor so the rect hangs from that corner or edge.
        static void PlaceCorner(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        static void Wire(SerializedObject so, string field, UnityEngine.Object value)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException($"GameShell has no serialized field '{field}'.");
            prop.objectReferenceValue = value;
        }

        static void WireArray<T>(SerializedObject so, string field, T[] values) where T : UnityEngine.Object
        {
            var prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException($"{so.targetObject.GetType().Name} has no serialized field '{field}'.");
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void EnsureEventSystem()
        {
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
                es = go.GetComponent<EventSystem>();
            }

            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);

            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
        }

        // ------------------------------------------------------------------ generated sprites

        static Sprite GetOrCreateRoundedRect()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedRectPath);
            if (existing != null) return existing;

            const int size = 64;
            const float radius = 16f;
            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded box; coverage fades over one pixel for anti-aliasing.
                    float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
                    float dist = outside + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - dist) * 255f);
                    // White texels (tint comes from the Image color); this is texture data, not a UI color.
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            return WriteSprite(RoundedRectPath, size, pixels, new Vector4(20f, 20f, 20f, 20f));
        }

        // White texels with alpha rising toward the screen edges; SurvivalHud tints it Danger.
        static Sprite GetOrCreateVignette()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(VignettePath);
            if (existing != null) return existing;

            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                    float ny = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                    float edge = Mathf.Max(nx, ny);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, edge));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            return WriteSprite(VignettePath, size, pixels, Vector4.zero);
        }

        static Sprite WriteSprite(string path, int size, Color32[] pixels, Vector4 border)
        {
            EnsureFolder(UiFolder);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
