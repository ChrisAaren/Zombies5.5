#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        const string ScrimPath = UiFolder + "/TopScrim.png";
        const string OutlineMaterialPath = UiFolder + "/UIText_Outline.mat";

        static Sprite s_RoundedRect;
        static Sprite s_Vignette;
        static Sprite s_Scrim;
        static Material s_Outline;

        // ------------------------------------------------------------------ public contract

        public static GameShell Build()
        {
            // Resources first, so a missing TMP import fails before we touch the scene.
            s_Outline = GetOrCreateOutlineMaterial();
            s_RoundedRect = GetOrCreateRoundedRect();
            s_Vignette = GetOrCreateVignette();
            s_Scrim = GetOrCreateScrim();

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

            var title = BuildTitle(root, out var promptText, out var titleSettings);
            var hud = BuildHud(root, out var survivalHud);
            var pauseParts = BuildPause(root);
            var pause = pauseParts.Root;
            var end = BuildEnded(root, out var bannerText, out var bannerRect, out var statsText,
                out var extraText, out var endRestart, out var endTitle);

            var settings = BuildSettings(root);

            // Overlays go last so they draw above everything.
            var flash = CreateOverlay(root, "FlashOverlay", Palette.WithAlpha(Palette.Accent, 0f));
            var fade = CreateOverlay(root, "FadeOverlay", Palette.WithAlpha(Palette.UiDark, 0f));

            var so = new SerializedObject(shell);
            Wire(so, "titleRoot", title);
            Wire(so, "promptText", promptText);
            Wire(so, "titleSettingsButton", titleSettings);
            Wire(so, "hudRoot", hud);
            Wire(so, "survivalHud", survivalHud);
            Wire(so, "pauseRoot", pause);
            Wire(so, "resumeButton", pauseParts.Resume);
            Wire(so, "pauseRestartButton", pauseParts.Restart);
            Wire(so, "pauseTitleButton", pauseParts.Title);
            Wire(so, "pauseSettingsButton", pauseParts.Settings);
            Wire(so, "pauseQuitButton", pauseParts.Quit);
            Wire(so, "pauseCard", pauseParts.Card);
            Wire(so, "quitConfirmRoot", pauseParts.Confirm);
            Wire(so, "quitConfirmButton", pauseParts.ConfirmQuit);
            Wire(so, "quitCancelButton", pauseParts.ConfirmCancel);
            Wire(so, "settingsMenu", settings);
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
            settings.gameObject.SetActive(false);
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

        static GameObject BuildTitle(RectTransform parent, out TMP_Text prompt, out UnityEngine.UI.Button settingsButton)
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
                // Design-time text only: ControlsHintText replaces it at runtime.
                "WASD MOVE · SHIFT SPRINT · SPACE JUMP · LMB FIRE · RMB AIM · R RELOAD · 1/2 SWAP · E BUY · ESC PAUSE",
                24, Palette.WithAlpha(Palette.UiLight, 0.85f), 2f, true);
            Place(controls.rectTransform, new Vector2(0.5f, 0.18f), Vector2.zero, new Vector2(1850f, 50f));
            controls.gameObject.AddComponent<ControlsHintText>();   // rewrites the line from the current bindings

            // Small settings button; the S key and gamepad Y open it too.
            settingsButton = CreateButton(root, "SettingsButton", "SETTINGS  (S)", new Vector2(0f, -440f),
                new Vector2(340f, 64f), 30);

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

            // A soft dark gradient behind the top HUD so the round counter and points stay readable over bright daylight and haze.
            var scrim = CreateImage(root, "TopScrim", s_Scrim, new Color(0.07f, 0.055f, 0.04f, 0.75f), false);
            var scrimRt = scrim.rectTransform;
            scrimRt.anchorMin = new Vector2(0f, 1f);
            scrimRt.anchorMax = new Vector2(1f, 1f);
            scrimRt.pivot = new Vector2(0.5f, 1f);
            scrimRt.anchoredPosition = Vector2.zero;
            scrimRt.sizeDelta = new Vector2(0f, 420f);

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

            var district = CreateText(root, "DistrictLabel", string.Empty, 28, Palette.Accent, 8f, true);
            district.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(district.rectTransform, new Vector2(0f, 1f), new Vector2(56f, -290f), new Vector2(620f, 36f));
            district.gameObject.SetActive(false);

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
            PlaceCorner(weaponName.rectTransform, top, new Vector2(24f, -14f), new Vector2(400f, 40f));

            // Tier accent: a thin bar down the panel's left edge and the tier tag (T0..T4) at the end of the name row.
            var tierBar = CreateImage(wp, "TierBar", s_RoundedRect, Palette.Tier(0), true);
            tierBar.pixelsPerUnitMultiplier = 3f;
            PlaceCorner(tierBar.rectTransform, top, new Vector2(8f, -14f), new Vector2(6f, 202f));
            var tierText = CreateText(wp, "TierTag", "T0", 26, Palette.Tier(0), 4f, true);
            tierText.alignment = TextAlignmentOptions.MidlineRight;
            PlaceCorner(tierText.rectTransform, top, new Vector2(432f, -14f), new Vector2(64f, 40f));

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

            // Crosshair: CrosshairView builds the ticks, dot and ring from GameSettings at runtime.
            var crosshair = NewRect("Crosshair", root);
            Place(crosshair, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var crosshairView = crosshair.gameObject.AddComponent<CrosshairView>();

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

            // District unlock banner: same look as the round banner, higher up.
            var districtBanner = NewRect("DistrictBanner", root);
            Place(districtBanner, new Vector2(0.5f, 0.83f), Vector2.zero, new Vector2(1700f, 120f));
            var districtGroup = districtBanner.gameObject.AddComponent<CanvasGroup>();
            districtGroup.interactable = false;
            districtGroup.blocksRaycasts = false;
            var districtBannerText = CreateText(districtBanner, "DistrictBannerText", "DISTRICT UNLOCKED", 38, Palette.Accent, 5f, true);
            Stretch(districtBannerText.rectTransform);

            BuildPickupHud(root, points.rectTransform);

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
            Wire(so, "districtText", district);
            WireArray(so, "healthPips", pips);
            Wire(so, "vignette", vignette);
            Wire(so, "pointsText", points);
            Wire(so, "pointsRect", points.rectTransform);
            Wire(so, "floatTemplate", floatTemplate);
            Wire(so, "floatRoot", floatRoot);
            Wire(so, "weaponNameText", weaponName);
            Wire(so, "weaponTierBar", tierBar);
            Wire(so, "weaponTierText", tierText);
            Wire(so, "magText", mag);
            Wire(so, "reserveText", reserve);
            WireArray(so, "slotChips", chips);
            WireArray(so, "slotLabels", chipLabels);
            Wire(so, "reloadBar", reloadBack.gameObject);
            Wire(so, "reloadFill", reloadFill);
            Wire(so, "crosshairRoot", crosshair.gameObject);
            Wire(so, "crosshairView", crosshairView);
            Wire(so, "hitMarkerGroup", hitGroup);
            Wire(so, "hitMarkerRect", hitRect);
            WireArray(so, "hitMarkerImages", hitImages);
            Wire(so, "promptText", prompt);
            Wire(so, "bannerRect", banner);
            Wire(so, "bannerGroup", bannerGroup);
            Wire(so, "bannerText", bannerText);
            Wire(so, "bannerSubText", bannerSub);
            Wire(so, "districtBannerRect", districtBanner);
            Wire(so, "districtBannerGroup", districtGroup);
            Wire(so, "districtBannerText", districtBannerText);
            so.ApplyModifiedPropertiesWithoutUndo();

            banner.gameObject.SetActive(false);
            districtBanner.gameObject.SetActive(false);
            return root.gameObject;
        }

        // Item drops: pickup announcement, effect chips with countdown bars (bottom center), x2 badge beside the points, and the
        // full-screen pulse. All driven by PickupHud.
        static void BuildPickupHud(RectTransform hudRoot, RectTransform points)
        {
            var root = CreateStretched("PickupHud", hudRoot);
            var hud = root.gameObject.AddComponent<PickupHud>();
            Color panelColor = Palette.WithAlpha(Palette.UiDark, 0.85f);
            Color soft = Palette.WithAlpha(Palette.UiLight, 0.85f);

            var overlay = CreateOverlay(root, "PickupPulse", Palette.WithAlpha(Color.white, 0f));

            // Announcement, upper middle: the drop's name in its color, a short line under it.
            var announce = NewRect("PickupAnnounce", root);
            Place(announce, new Vector2(0.5f, 0.4f), Vector2.zero, new Vector2(1500f, 200f));
            var announceGroup = announce.gameObject.AddComponent<CanvasGroup>();
            announceGroup.interactable = false;
            announceGroup.blocksRaycasts = false;
            var announceText = CreateText(announce, "PickupName", "DEADEYE", 112, Palette.PickupRed, 10f, true);
            Place(announceText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(1500f, 130f));
            var announceSub = CreateText(announce, "PickupSub", string.Empty, 42, Palette.UiLight, 6f, true);
            Place(announceSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(1500f, 56f));

            // Effect chips: name, seconds left, and a draining bar.
            string[] names = { "DEADEYE", "PAYDAY" };
            string[] hints = { "ONE SHOT", "DOUBLE POINTS" };
            Color[] colors = { Palette.PickupRed, Palette.PickupLime };
            var chipRects = new RectTransform[2];
            var chipGroups = new CanvasGroup[2];
            var chipFills = new UnityEngine.UI.Image[2];
            var chipAccents = new UnityEngine.UI.Image[2];
            var chipTimes = new TMP_Text[2];
            for (int i = 0; i < 2; i++)
            {
                var chip = CreateImage(root, "EffectChip_" + names[i], s_RoundedRect, panelColor, true);
                PlaceCorner(chip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(300f, 78f));
                chip.rectTransform.pivot = new Vector2(0.5f, 0f);
                chipRects[i] = chip.rectTransform;
                chipGroups[i] = chip.gameObject.AddComponent<CanvasGroup>();
                chipGroups[i].interactable = false;
                chipGroups[i].blocksRaycasts = false;

                var accent = CreateImage(chip.rectTransform, "Accent", s_RoundedRect, colors[i], true);
                PlaceCorner(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(8f, 54f));
                chipAccents[i] = accent;

                var label = CreateText(chip.rectTransform, "Name", names[i], 28, colors[i], 5f, false);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                PlaceCorner(label.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -8f), new Vector2(180f, 34f));
                var hint = CreateText(chip.rectTransform, "Hint", hints[i], 18, soft, 3f, false);
                hint.alignment = TextAlignmentOptions.MidlineLeft;
                PlaceCorner(hint.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -40f), new Vector2(200f, 22f));
                var time = CreateText(chip.rectTransform, "Time", "30S", 32, Palette.UiLight, 2f, false);
                time.alignment = TextAlignmentOptions.MidlineRight;
                PlaceCorner(time.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(100f, 40f));
                chipTimes[i] = time;

                var back = CreateImage(chip.rectTransform, "BarBack", s_RoundedRect, Palette.Shadow, true);
                back.pixelsPerUnitMultiplier = 2f;
                PlaceCorner(back.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 10f), new Vector2(256f, 10f));
                var fill = CreateImage(back.rectTransform, "BarFill", s_RoundedRect, colors[i], false);
                fill.type = UnityEngine.UI.Image.Type.Filled;
                fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
                fill.fillAmount = 1f;
                Stretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(1f, 1f);
                fill.rectTransform.offsetMax = new Vector2(-1f, -1f);
                chipFills[i] = fill;
            }

            // x2 badge beside the points readout, left of the POINTS label.
            var badge = CreateImage(root, "PaydayBadge", s_RoundedRect, Palette.PickupLime, true);
            PlaceCorner(badge.rectTransform, new Vector2(1f, 1f), new Vector2(-236f, -32f), new Vector2(96f, 44f));
            var badgeGroup = badge.gameObject.AddComponent<CanvasGroup>();
            badgeGroup.interactable = false;
            badgeGroup.blocksRaycasts = false;
            var badgeText = CreateText(badge.rectTransform, "BadgeText", "x2", 36, Palette.UiDark, 1f, false);
            Stretch(badgeText.rectTransform);

            var so = new SerializedObject(hud);
            Wire(so, "announceRect", announce);
            Wire(so, "announceGroup", announceGroup);
            Wire(so, "announceText", announceText);
            Wire(so, "announceSub", announceSub);
            WireArray(so, "chipRects", chipRects);
            WireArray(so, "chipGroups", chipGroups);
            WireArray(so, "chipFills", chipFills);
            WireArray(so, "chipAccents", chipAccents);
            WireArray(so, "chipTimes", chipTimes);
            Wire(so, "badgeRect", badge.rectTransform);
            Wire(so, "badgeGroup", badgeGroup);
            Wire(so, "overlay", overlay);
            so.ApplyModifiedPropertiesWithoutUndo();

            announce.gameObject.SetActive(false);
            for (int i = 0; i < 2; i++) chipRects[i].gameObject.SetActive(false);
            badge.gameObject.SetActive(false);
        }

        sealed class PauseParts
        {
            public GameObject Root, Card, Confirm;
            public UnityEngine.UI.Button Resume, Settings, Restart, Title, Quit, ConfirmQuit, ConfirmCancel;
        }

        static PauseParts BuildPause(RectTransform parent)
        {
            var p = new PauseParts();
            var root = CreateStretched("PauseScreen", parent);
            p.Root = root.gameObject;

            var scrim = CreateImage(root, "Scrim", null, Palette.WithAlpha(Palette.UiDark, 0.7f), true);
            scrim.raycastTarget = true; // blocks clicks behind the menu
            Stretch(scrim.rectTransform);

            var card = CreateImage(root, "Card", s_RoundedRect, Palette.WithAlpha(Palette.UiDark, 0.8f), true);
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 800f));
            p.Card = card.gameObject;

            var heading = CreateText(card.rectTransform, "Heading", "PAUSED", 110, Palette.Accent, 8f, true);
            Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(700f, 140f));

            p.Resume = CreateButton(card.rectTransform, "ResumeButton", "RESUME", new Vector2(0f, 140f));
            p.Settings = CreateButton(card.rectTransform, "SettingsButton", "SETTINGS", new Vector2(0f, 30f));
            p.Restart = CreateButton(card.rectTransform, "RestartButton", "RESTART", new Vector2(0f, -80f));
            p.Title = CreateButton(card.rectTransform, "TitleButton", "TITLE", new Vector2(0f, -190f));
            p.Quit = CreateButton(card.rectTransform, "QuitButton", "QUIT GAME", new Vector2(0f, -300f));

            // Confirmation: replaces the pause card while it is open.
            var confirm = CreateImage(root, "QuitConfirm", s_RoundedRect, Palette.WithAlpha(Palette.UiDark, 0.85f), true);
            Place(confirm.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 500f));
            p.Confirm = confirm.gameObject;
            var ch = CreateText(confirm.rectTransform, "Heading", "QUIT TO DESKTOP?", 70, Palette.Accent, 6f, true);
            Place(ch.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(860f, 110f));
            var msg = CreateText(confirm.rectTransform, "Message", "Your current run will be lost.", 32,
                Palette.WithAlpha(Palette.UiLight, 0.9f), 2f, true);
            msg.fontStyle = FontStyles.Normal;
            Place(msg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(800f, 50f));
            p.ConfirmQuit = CreateButton(confirm.rectTransform, "QuitConfirmButton", "QUIT", new Vector2(-200f, -130f), new Vector2(340f, 90f));
            p.ConfirmCancel = CreateButton(confirm.rectTransform, "QuitCancelButton", "CANCEL", new Vector2(200f, -130f), new Vector2(340f, 90f));
            p.Confirm.SetActive(false);
            return p;
        }

        // ------------------------------------------------------------------ settings screen

        static SettingsMenu BuildSettings(RectTransform parent)
        {
            var root = CreateStretched("SettingsScreen", parent);
            var menu = root.gameObject.AddComponent<SettingsMenu>();

            var scrim = CreateImage(root, "Scrim", null, Palette.WithAlpha(Palette.UiDark, 0.85f), true);
            scrim.raycastTarget = true;
            Stretch(scrim.rectTransform);

            var card = CreateImage(root, "Card", s_RoundedRect, Palette.WithAlpha(Palette.UiDark, 0.92f), true);
            card.raycastTarget = true;
            var cardRt = card.rectTransform;
            Place(cardRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560f, 940f));

            var heading = CreateText(cardRt, "Heading", "SETTINGS", 90, Palette.Accent, 8f, true);
            Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 395f), new Vector2(700f, 110f));

            const int TabCount = 6;
            string[] tabNames = { "CONTROLS", "KEYBINDS", "CROSSHAIR", "AUDIO", "GRAPHICS", "BENCHMARK" };
            var tabButtons = new UnityEngine.UI.Button[TabCount];
            var tabPanels = new GameObject[TabCount];
            var firsts = new UnityEngine.UI.Selectable[TabCount];
            for (int i = 0; i < TabCount; i++)
            {
                // Six tabs keep the 27.5 px side margins the five had: 238 wide, 15 apart.
                tabButtons[i] = CreateButton(cardRt, tabNames[i] + "Tab", tabNames[i],
                    new Vector2(-633.5f + i * 253.4f, 300f), new Vector2(238f, 66f), 26f);
                tabPanels[i] = CreateStretched(tabNames[i] + "Panel", cardRt).gameObject;
            }

            // Controls
            var controls = (RectTransform)tabPanels[0].transform;
            firsts[0] = AddSliderRow(controls, SettingId.MouseSensitivity, "MOUSE SENSITIVITY", new Vector2(0f, 160f), 1200f, 70f);
            AddSliderRow(controls, SettingId.AimSensitivity, "AIM SENSITIVITY", new Vector2(0f, 70f), 1200f, 70f);
            AddSliderRow(controls, SettingId.ControllerSensitivity, "CONTROLLER LOOK", new Vector2(0f, -20f), 1200f, 70f);
            AddToggleRow(controls, SettingId.InvertY, "INVERT Y AXIS", new Vector2(0f, -110f), 1200f, 70f);

            // Keybinds
            var keybinds = (RectTransform)tabPanels[1].transform;
            firsts[1] = BuildKeybindsTab(keybinds, out var bindingRows);

            // Crosshair: preview on the left, options on the right
            var cross = (RectTransform)tabPanels[2].transform;
            var previewPanel = CreateImage(cross, "PreviewPanel", s_RoundedRect, Palette.Asphalt, true);
            Place(previewPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-520f, -10f), new Vector2(480f, 460f));
            var patch = CreateImage(previewPanel.rectTransform, "PreviewPatch", null, Palette.Sand, false);
            SetAnchors(patch.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            patch.rectTransform.offsetMin = new Vector2(0f, 16f);
            patch.rectTransform.offsetMax = new Vector2(-16f, -16f);
            var previewLabel = CreateText(previewPanel.rectTransform, "PreviewLabel", "PREVIEW", 24,
                Palette.WithAlpha(Palette.UiLight, 0.9f), 8f, true);
            PlaceCorner(previewLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 30f));
            var previewRoot = NewRect("PreviewCrosshair", previewPanel.rectTransform);
            Place(previewRoot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var previewView = previewRoot.gameObject.AddComponent<CrosshairView>();
            var pso = new SerializedObject(previewView);
            pso.FindProperty("previewPulse").boolValue = true;
            pso.ApplyModifiedPropertiesWithoutUndo();

            float rx = 300f, rw = 860f, rh = 52f, y = 215f, dy = 56f;
            firsts[2] = AddStepperRow(cross, SettingId.CrosshairStyle, "STYLE", new Vector2(rx, y), rw, rh, false);
            AddStepperRow(cross, SettingId.CrosshairColor, "COLOR", new Vector2(rx, y - dy), rw, rh, true);
            AddSliderRow(cross, SettingId.CrosshairSize, "SIZE", new Vector2(rx, y - dy * 2), rw, rh);
            AddSliderRow(cross, SettingId.CrosshairGap, "GAP", new Vector2(rx, y - dy * 3), rw, rh);
            AddSliderRow(cross, SettingId.CrosshairLength, "LINE LENGTH", new Vector2(rx, y - dy * 4), rw, rh);
            AddSliderRow(cross, SettingId.CrosshairThickness, "THICKNESS", new Vector2(rx, y - dy * 5), rw, rh);
            AddSliderRow(cross, SettingId.CrosshairOpacity, "OPACITY", new Vector2(rx, y - dy * 6), rw, rh);
            AddToggleRow(cross, SettingId.CrosshairOutline, "OUTLINE", new Vector2(rx, y - dy * 7), rw, rh);
            AddToggleRow(cross, SettingId.CrosshairDot, "CENTER DOT", new Vector2(rx, y - dy * 8), rw, rh);
            AddToggleRow(cross, SettingId.CrosshairDynamic, "DYNAMIC SPREAD", new Vector2(rx, y - dy * 9), rw, rh);

            // Audio
            var audio = (RectTransform)tabPanels[3].transform;
            firsts[3] = AddSliderRow(audio, SettingId.MusicVolume, "MUSIC VOLUME", new Vector2(0f, 90f), 1200f, 70f);
            AddSliderRow(audio, SettingId.SfxVolume, "SFX VOLUME", new Vector2(0f, 0f), 1200f, 70f);

            // Graphics: a scrolling list in five groups.
            var gfx = (RectTransform)tabPanels[4].transform;
            var scroll = BuildScrollList(gfx, "GraphicsScroll", new Vector2(0f, -2f), new Vector2(1380f, 520f), out var content, true);
            const float gw = 1200f, gh = 54f;
            AddHeader(content, "DISPLAY");
            // The monitor row is greyed out with a single display, so the mode row takes the initial focus.
            AddStepperRow(content, SettingId.Monitor, "MONITOR", default, gw, gh, false, 560f, true, "Which monitor the game runs on. Greyed out when only one is connected.");
            firsts[4] = AddStepperRow(content, SettingId.DisplayMode, "DISPLAY MODE", default, gw, gh, false, 560f, true, "Fullscreen is borderless and always uses the monitor's current refresh rate. Windowed keeps a normal window.");
            AddStepperRow(content, SettingId.Resolution, "RESOLUTION", default, gw, gh, false, 560f, true, "Output resolution of the chosen monitor. Lower it for speed, or use Render Scale to keep the desktop resolution.");
            AddStepperRow(content, SettingId.FrameRate, "FRAME RATE", default, gw, gh, false, 260f, true, "Caps the frame rate. Unlimited removes the cap (with VSync off, frames can tear).");
            AddToggleRow(content, SettingId.VSync, "VSYNC", default, gw, gh, 0.34f, true, "Waits for the display refresh: no tearing, a little more input latency. Off gives the lowest latency.");
            AddSliderRow(content, SettingId.Fov, "FIELD OF VIEW", default, gw, gh, true, "Camera field of view. Wider shows more of the street but renders more objects (a small cost).");
            AddSliderRow(content, SettingId.Brightness, "BRIGHTNESS", default, gw, gh, true, "Overall exposure of the image, in stops. Zero is the intended look.");
            AddToggleRow(content, SettingId.FpsCounter, "FPS COUNTER", default, gw, gh, 0.34f, true, "Shows the frame rate and frame time at the top of the screen.");

            AddHeader(content, "QUALITY PRESET");
            AddStepperRow(content, SettingId.QualityPreset, "PRESET", default, gw, gh, false, 260f, true, "Sets every quality option below at once. Changing any of them afterwards switches this to Custom.");

            AddHeader(content, "RENDERING");
            AddStepperRow(content, SettingId.RenderScale, "RENDER SCALE", default, gw, gh, false, 260f, true, "Resolution the 3D scene renders at. Lower is faster (GPU), 125% is supersampling for a cleaner image.");
            AddStepperRow(content, SettingId.Upscaler, "UPSCALER", default, gw, gh, false, 260f, true, "Sharpens a lowered Render Scale with AMD FSR 1.0. Only used below 100%.");
            AddStepperRow(content, SettingId.AntiAliasing, "ANTI-ALIASING", default, gw, gh, false, 260f, true, "Smooths jagged edges. FXAA is cheapest, SMAA is sharper, TAA is the cleanest and the default look.");
            AddStepperRow(content, SettingId.ShadowQuality, "SHADOW QUALITY", default, gw, gh, false, 260f, true, "Shadow map resolution, softness and cascade count. The biggest quality-for-speed trade (CPU and GPU).");
            AddStepperRow(content, SettingId.ShadowDistance, "SHADOW DISTANCE", default, gw, gh, false, 260f, true, "How far from the camera shadows are drawn. Shorter is much faster (CPU).");
            AddStepperRow(content, SettingId.AmbientOcclusion, "AMBIENT OCCLUSION", default, gw, gh, false, 260f, true, "Darkens creases and contact points (props on the street, under cars). Low cost on the GPU.");
            AddStepperRow(content, SettingId.TextureQuality, "TEXTURE QUALITY", default, gw, gh, false, 260f, true, "Low halves texture resolution to save graphics memory. Changing it takes a moment.");
            AddStepperRow(content, SettingId.ObjectDetail, "OBJECT DETAIL", default, gw, gh, false, 260f, true, "How far away cars and props keep their detailed models (CPU and GPU).");

            AddHeader(content, "POST-PROCESSING");
            AddStepperRow(content, SettingId.Bloom, "BLOOM", default, gw, gh, false, 260f, true, "Soft glow around bright lights and the sky. High Quality uses a sharper filter.");
            AddToggleRow(content, SettingId.DepthOfField, "DEPTH OF FIELD", default, gw, gh, 0.34f, true, "Softly blurs the far skyline. Everything within combat range stays sharp.");
            AddToggleRow(content, SettingId.MotionBlur, "MOTION BLUR", default, gw, gh, 0.34f, true, "Blurs the image while the camera turns quickly. Off in the default look.");
            AddToggleRow(content, SettingId.LensFlare, "LENS FLARE", default, gw, gh, 0.34f, true, "Glare and ghosts from the sun and bright lights.");
            AddToggleRow(content, SettingId.FilmGrain, "FILM GRAIN", default, gw, gh, 0.34f, true, "A fine grain over the image for a filmic texture.");

            AddHeader(content, "ADVANCED");
            AddStepperRow(content, SettingId.StreetDetail, "STREET DETAIL", default, gw, gh, false, 260f, true, "Dirt, posters, litter, steam and cables, and how far away they are drawn (CPU).");
            AddStepperRow(content, SettingId.EffectsQuality, "EFFECTS QUALITY", default, gw, gh, false, 260f, true, "Explosion particles, debris, smoke, lights, wrecks, scorch marks and ragdolls (CPU and GPU).");
            AddStepperRow(content, SettingId.CharacterDetail, "CHARACTER DETAIL", default, gw, gh, false, 260f, true, "Skinning quality and foot placement on the enemy crowd (CPU).");
            AddStepperRow(content, SettingId.Anisotropic, "ANISOTROPIC FILTERING", default, gw, gh, false, 260f, true, "Keeps road and wall textures sharp at a slant. Almost free on modern GPUs.");

            AddHeader(content, "BENCHMARK");
            var gfxBench = CreateButton(content, "RunBenchmarkRow", "RUN BENCHMARK  >", Vector2.zero, new Vector2(gw, 62f), 30f);
            var gbl = gfxBench.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            gbl.preferredWidth = gw; gbl.preferredHeight = 62f;
            AddDescription(gfxBench.gameObject, "Measure how this PC runs the game with these settings: a 60 second route through the city.");

            // Benchmark tab
            var bench = (RectTransform)tabPanels[5].transform;
            var benchPanel = BuildBenchmarkTab(bench, out var runBench);
            firsts[5] = runBench;

            var description = CreateText(cardRt, "Description", string.Empty, 24,
                Palette.WithAlpha(Palette.UiLight, 0.92f), 1f, true);
            description.fontStyle = FontStyles.Normal;
            Place(description.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -337f), new Vector2(1440f, 30f));

            var reset = CreateButton(cardRt, "ResetButton", "RESET TO DEFAULTS", new Vector2(-250f, -388f), new Vector2(440f, 58f), 32f);
            var back = CreateButton(cardRt, "BackButton", "BACK", new Vector2(250f, -388f), new Vector2(440f, 58f), 34f);

            var hint = CreateText(cardRt, "Hint", "LB / RB  TABS        ESC / B  BACK", 22,
                Palette.WithAlpha(Palette.UiLight, 0.7f), 6f, true);
            Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -446f), new Vector2(900f, 28f));

            LinkKeybindsNavigation(bindingRows, tabButtons[1], reset, back);

            var so = new SerializedObject(menu);
            WireArray(so, "tabButtons", tabButtons);
            WireArray(so, "tabPanels", tabPanels);
            WireArray(so, "firstSelectable", firsts);
            Wire(so, "resetButton", reset);
            Wire(so, "backButton", back);
            Wire(so, "card", cardRt);
            Wire(so, "descriptionText", description);
            Wire(so, "benchmarkPanel", benchPanel);
            Wire(so, "graphicsBenchmarkButton", gfxBench);
            Wire(so, "graphicsScroll", scroll);
            so.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 1; i < TabCount; i++) tabPanels[i].SetActive(false);
            return menu;
        }

        // ------------------------------------------------------------------ keybinds tab

        const float BindRowW = 1200f, BindRowH = 54f, BindLabelW = 350f, BindSlotX = 380f, BindSlotW = 256f, BindSlotStep = 272f;

        sealed class BindRowInfo
        {
            public SettingsBindingRow Row;
            public UnityEngine.UI.Button[] Slots;
        }

        // Column headings, then the scrolling list: groups of rebindable rows, then the read-only fixed controls.
        static UnityEngine.UI.Selectable BuildKeybindsTab(RectTransform panel, out List<BindRowInfo> rows)
        {
            rows = new List<BindRowInfo>();
            BuildScrollList(panel, "KeybindsScroll", new Vector2(0f, -27f), new Vector2(1380f, 470f), out var content, true);

            // Headings sit above the list, centred over the keycaps. The viewport is the list minus the scrollbar gutter.
            float rowLeft = -9f - BindRowW * 0.5f;
            var action = CreateText(panel, "ActionHeading", "ACTION", 20, Palette.Accent, 3f, true);
            action.alignment = TextAlignmentOptions.MidlineLeft;
            Place(action.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(rowLeft + 22f + BindLabelW * 0.5f, 232f), new Vector2(BindLabelW, 30f));
            string[] heads = { "KEYBOARD / MOUSE", "ALTERNATE", "GAMEPAD" };
            for (int i = 0; i < 3; i++)
            {
                var h = CreateText(panel, "SlotHeading" + i, heads[i], 20, Palette.Accent, 3f, true);
                Place(h.rectTransform, new Vector2(0.5f, 0.5f),
                    new Vector2(rowLeft + BindSlotX + BindSlotW * 0.5f + i * BindSlotStep, 232f), new Vector2(BindSlotW, 30f));
            }
            var rule = CreateImage(panel, "HeadingRule", null, Palette.WithAlpha(Palette.Accent, 0.55f), false);
            Place(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-9f, 214f), new Vector2(BindRowW, 2f));

            AddHeader(content, "MOVEMENT");
            foreach (var a in new[] { GameAction.MoveForward, GameAction.MoveBack, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Sprint, GameAction.Jump })
                rows.Add(AddBindingRow(content, a));
            AddHeader(content, "COMBAT");
            foreach (var a in new[] { GameAction.Fire, GameAction.Aim, GameAction.Reload, GameAction.Weapon1, GameAction.Weapon2, GameAction.NextWeapon })
                rows.Add(AddBindingRow(content, a));
            AddHeader(content, "GENERAL");
            foreach (var a in new[] { GameAction.Interact, GameAction.Pause })
                rows.Add(AddBindingRow(content, a));

            AddHeader(content, "FIXED CONTROLS");
            AddFixedRow(content, "LOOK", "MOUSE", "—", "R-STICK");
            AddFixedRow(content, "START GAME (TITLE)", "ANY KEY / LMB", "—", "A / START");
            AddFixedRow(content, "OPEN SETTINGS (TITLE)", "S", "—", "Y");
            AddFixedRow(content, "BACK / CANCEL", "ESC", "—", "B / START");
            AddFixedRow(content, "RESTART (AFTER DEATH)", "R / ENTER", "—", "A");   // the end screen focuses its RESTART button
            AddFixedRow(content, "SETTINGS TABS", "—", "—", "LB / RB");
            AddFixedRow(content, "MENU MOVE", "ARROWS / WASD", "—", "D-PAD / L-STICK");
            AddFixedRow(content, "MENU SELECT", "ENTER", "—", "A");

            return rows[0].Slots[0];
        }

        static BindRowInfo AddBindingRow(RectTransform content, GameAction a)
        {
            var bg = CreateRowBase(content, a + "BindRow", GameInput.Label(a), default, BindRowW, BindRowH, false, BindLabelW / BindRowW, true);
            bg.color = Palette.WithAlpha(Palette.Shadow, 0.4f);
            var label = bg.rectTransform.Find("Label").GetComponent<TextMeshProUGUI>();
            label.fontSize = 26f; label.characterSpacing = 2f;

            var buttons = new UnityEngine.UI.Button[3];
            var texts = new TMP_Text[3];
            var images = new UnityEngine.UI.Image[3];
            var descs = new string[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = (BindingSlot)i;
                var b = CreateButton(bg.rectTransform, slot + "Slot", "—", Vector2.zero, new Vector2(BindSlotW, 42f), 26f);
                PlaceCorner((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(BindSlotX + i * BindSlotStep, 0f), new Vector2(BindSlotW, 42f));
                // Keycap: light cap, accent when focused, a dim dark cap when the slot is fixed.
                var colors = b.colors;
                colors.normalColor = Palette.UiLight;
                colors.highlightedColor = Color.Lerp(Palette.UiLight, Palette.Accent, 0.55f);
                colors.selectedColor = Palette.Accent;
                colors.pressedColor = Color.Lerp(Palette.Accent, Palette.UiDark, 0.25f);
                colors.disabledColor = Palette.WithAlpha(Palette.UiDark, 0.55f);
                b.colors = colors;
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = 26f;
                t.characterSpacing = 2f;
                t.rectTransform.offsetMin = new Vector2(8f, 0f);
                t.rectTransform.offsetMax = new Vector2(-8f, 0f);
                buttons[i] = b; texts[i] = t; images[i] = b.GetComponent<UnityEngine.UI.Image>();
                descs[i] = BindingSlotDescription(a, slot);
                AddDescription(b.gameObject, descs[i]);
            }

            var row = bg.gameObject.AddComponent<SettingsBindingRow>();
            var so = new SerializedObject(row);
            so.FindProperty("action").enumValueIndex = (int)a;
            WireArray(so, "slotButtons", buttons);
            WireArray(so, "slotTexts", texts);
            WireArray(so, "slotImages", images);
            var dp = so.FindProperty("slotDescriptions");
            dp.arraySize = 3;
            for (int i = 0; i < 3; i++) dp.GetArrayElementAtIndex(i).stringValue = descs[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            // Locked slots are not selectable (the row also sets this at runtime).
            for (int i = 0; i < 3; i++) buttons[i].interactable = GameInput.IsRebindable(a, (BindingSlot)i);
            return new BindRowInfo { Row = row, Slots = buttons };
        }

        static string BindingSlotDescription(GameAction a, BindingSlot slot)
        {
            if (GameInput.IsRebindable(a, slot)) return GameInput.Description(a) + " Enter / A to change.";
            if (a == GameAction.Pause) return slot == BindingSlot.Gamepad ? "Start always pauses and goes back." : "Esc always pauses and goes back.";
            return "The gamepad left stick always moves. It cannot be changed.";
        }

        // Read-only line: plain, dimmer text, not selectable.
        static void AddFixedRow(RectTransform content, string label, string kb, string alt, string pad)
        {
            var bg = CreateRowBase(content, label + "FixedRow", label, default, BindRowW, 46f, false, BindLabelW / BindRowW, true);
            bg.color = Palette.WithAlpha(Palette.Shadow, 0.18f);
            var l = bg.rectTransform.Find("Label").GetComponent<TextMeshProUGUI>();
            l.fontSize = 24f; l.characterSpacing = 2f; l.color = Palette.WithAlpha(Palette.UiLight, 0.6f);
            string[] cells = { kb, alt, pad };
            for (int i = 0; i < 3; i++)
            {
                var t = CreateText(bg.rectTransform, "Cell" + i, cells[i], 22f, Palette.WithAlpha(Palette.UiLight, 0.6f), 2f, false);
                t.enableAutoSizing = true; t.fontSizeMin = 14f; t.fontSizeMax = 22f;
                PlaceCorner(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(BindSlotX + i * BindSlotStep, 0f), new Vector2(BindSlotW, 46f));
            }
        }

        // Explicit navigation: up / down stays in the column (nearest column when a slot is fixed), left / right moves along the row.
        static void LinkKeybindsNavigation(List<BindRowInfo> rows, UnityEngine.UI.Selectable tab,
            UnityEngine.UI.Button reset, UnityEngine.UI.Button back)
        {
            UnityEngine.UI.Selectable Find(int r, int dir, int col)
            {
                for (r += dir; r >= 0 && r < rows.Count; r += dir)
                {
                    UnityEngine.UI.Button best = null; int bestD = 99;
                    for (int c = 0; c < 3; c++)
                        if (rows[r].Slots[c].interactable && Mathf.Abs(c - col) < bestD) { best = rows[r].Slots[c]; bestD = Mathf.Abs(c - col); }
                    if (best != null) return best;
                }
                return null;
            }
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < 3; c++)
                {
                    var b = rows[r].Slots[c];
                    if (!b.interactable) continue;
                    UnityEngine.UI.Selectable left = null, right = null;
                    for (int k = c - 1; k >= 0 && left == null; k--) if (rows[r].Slots[k].interactable) left = rows[r].Slots[k];
                    for (int k = c + 1; k < 3 && right == null; k++) if (rows[r].Slots[k].interactable) right = rows[r].Slots[k];
                    var nav = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
                    nav.selectOnLeft = left;
                    nav.selectOnRight = right;
                    nav.selectOnUp = Find(r, -1, c) ?? tab;
                    nav.selectOnDown = Find(r, 1, c) ?? (c == 2 ? back : reset);
                    b.navigation = nav;
                }
        }

        // ------------------------------------------------------------------ scrolling lists

        // ScrollRect with a clipped viewport, a vertical layout content and a slim scrollbar. follow = gamepad focus scrolls the list.
        static UnityEngine.UI.ScrollRect BuildScrollList(RectTransform parent, string name, Vector2 pos, Vector2 size, out RectTransform content, bool follow)
        {
            var rt = NewRect(name, parent);
            Place(rt, new Vector2(0.5f, 0.5f), pos, size);
            var scroll = rt.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = NewRect("Viewport", rt);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-22f, 0f);   // room for the scrollbar
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            // A raycastable transparent image so the mouse wheel scrolls anywhere over the list.
            var hit = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var vlg = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(0, 0, 4, 10);
            var fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            // Scrollbar
            var barRt = NewRect("Scrollbar", rt);
            SetAnchors(barRt, new Vector2(1f, 0f), new Vector2(1f, 1f));
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.sizeDelta = new Vector2(12f, 0f);
            barRt.anchoredPosition = Vector2.zero;
            var track = barRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            track.sprite = s_RoundedRect; track.type = UnityEngine.UI.Image.Type.Sliced;
            track.color = Palette.WithAlpha(Palette.Shadow, 0.7f);
            var slide = NewRect("Sliding Area", barRt);
            Stretch(slide);
            var handle = CreateImage(slide, "Handle", s_RoundedRect, Palette.WithAlpha(Palette.UiLight, 0.55f), true);
            handle.raycastTarget = true;
            Stretch(handle.rectTransform);
            var bar = barRt.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            var nav = bar.navigation; nav.mode = UnityEngine.UI.Navigation.Mode.None; bar.navigation = nav;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 6f;
            if (follow) rt.gameObject.AddComponent<SettingsScrollFollow>();
            return scroll;
        }

        // Group title with a thin accent rule: not selectable.
        static void AddHeader(RectTransform content, string title)
        {
            var rt = NewRect(title + "Header", content);
            var le = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = 1200f; le.preferredHeight = 50f;
            var text = CreateText(rt, "Title", title, 26, Palette.Accent, 8f, true);
            text.alignment = TextAlignmentOptions.BottomLeft;
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
            text.rectTransform.offsetMin = new Vector2(8f, 6f);
            text.rectTransform.offsetMax = Vector2.zero;
            var line = CreateImage(rt, "Rule", null, Palette.WithAlpha(Palette.Accent, 0.55f), false);
            SetAnchors(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f));
            line.rectTransform.pivot = new Vector2(0.5f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);
            line.rectTransform.anchoredPosition = Vector2.zero;
        }

        static void AddDescription(GameObject target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var d = target.AddComponent<SettingDescription>();
            var so = new SerializedObject(d);
            so.FindProperty("text").stringValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ benchmark tab

        static TextMeshProUGUI BodyText(RectTransform parent, string name, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align, bool wrap = true)
        {
            var t = CreateText(parent, name, string.Empty, fontSize, Palette.UiLight, 1f, false);
            t.fontStyle = FontStyles.Normal;
            t.alignment = align;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
            return t;
        }

        // Text box placed by its top-left corner, in the coordinates of a stretched ancestor (origin = centre, y up).
        static TextMeshProUGUI TextAt(RectTransform parent, string name, float x, float yTop, float w, float h, float size,
            TextAlignmentOptions align, Color color, bool wrap = true, bool bold = false)
        {
            var t = BodyText(parent, name, new Vector2(x + w * 0.5f, yTop - h * 0.5f), new Vector2(w, h), size, align, wrap);
            t.color = color;
            if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }

        // Column title: accent caps with a thin rule under it.
        static void ColumnHeading(RectTransform parent, string title, float x, float yTop, float w)
        {
            var t = CreateText(parent, title + "Title", title, 24, Palette.Accent, 6f, true);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x + w * 0.5f, yTop - 17f), new Vector2(w, 34f));
            var rule = CreateImage(parent, title + "Rule", null, Palette.WithAlpha(Palette.Accent, 0.55f), false);
            Place(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x + w * 0.5f, yTop - 38f), new Vector2(w, 2f));
        }

        static UnityEngine.UI.Button TabButton(RectTransform parent, string name, string label, ref float x, float w, float y, float font)
        {
            var b = CreateButton(parent, name, label, new Vector2(x + w * 0.5f, y), new Vector2(w, 56f), font);
            x += w + 20f;
            return b;
        }

        static BenchmarkPanel BuildBenchmarkTab(RectTransform tab, out UnityEngine.UI.Selectable runButton)
        {
            var panel = tab.gameObject.AddComponent<BenchmarkPanel>();
            var dim = Palette.WithAlpha(Palette.UiLight, 0.8f);
            const float L = -730f, Top = 255f;

            // ---- home: three columns (settings | run | past runs)
            var home = CreateStretched("Home", tab);
            ColumnHeading(home, "YOUR SETTINGS", L, Top, 440f);
            var presetText = TextAt(home, "Preset", L, Top - 50f, 440f, 90f, 76f, TextAlignmentOptions.MidlineLeft, Palette.UiLight, false, true);
            var displayText = TextAt(home, "Display", L, Top - 142f, 440f, 34f, 24f, TextAlignmentOptions.MidlineLeft, dim, false);
            var changed = TextAt(home, "Changed", L, Top - 196f, 440f, 210f, 24f, TextAlignmentOptions.TopLeft, Palette.UiLight);
            changed.lineSpacing = 8f;

            var run = CreateButton(home, "RunButton", "RUN BENCHMARK", new Vector2(0f, 150f), new Vector2(500f, 100f), 42f);
            AddToggleRow(home, SettingId.BenchUncapped, "UNCAPPED FPS", new Vector2(0f, 40f), 500f, 54f, 0.6f, false,
                "Ignores the frame cap and VSync during the run, to measure what this PC can really do.");
            AddToggleRow(home, SettingId.BenchExplosions, "EXPLOSIONS", new Vector2(0f, -26f), 500f, 54f, 0.6f, false,
                "Blows up a car or barrel near the route every few seconds: a harder test for the CPU and effects.");
            var note = TextAt(home, "Note", -250f, -70f, 500f, 64f, 21f, TextAlignmentOptions.Top, dim);

            const float HX = 290f, HW = 440f;
            ColumnHeading(home, "PAST RUNS", HX, Top, HW);
            // Column titles of the table, aligned with the row cells below.
            float[] cx = { 14f, 160f, 244f, 316f }, cw = { 140f, 84f, 64f, 92f };
            string[] titles = { "DATE", "PRESET", "AVG", "1% LOW" };
            for (int i = 0; i < 4; i++)
            {
                var h = TextAt(home, "Col" + titles[i].Replace("% ", ""), HX + cx[i], Top - 46f, cw[i], 26f, 15f,
                    i >= 2 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, dim, false, true);
                h.characterSpacing = 1f;
                h.text = titles[i];
            }
            var histScroll = BuildScrollList(home, "HistoryScroll", new Vector2(HX + HW * 0.5f, -45f), new Vector2(HW, 434f), out var histContent, true);
            histScroll.GetComponent<SettingsScrollFollow>().enabled = true;
            // Keep the scrollbar's space even when the list is short, so the rows stay under the column titles.
            histScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
            var template = CreateButton(histContent, "RowTemplate", "ROW", Vector2.zero, new Vector2(418f, 44f), 18f);
            var tl = template.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            tl.preferredWidth = 418f; tl.preferredHeight = 44f;
            // Four aligned cells instead of the single label.
            UnityEngine.Object.DestroyImmediate(template.GetComponentInChildren<TextMeshProUGUI>().gameObject);
            for (int i = 0; i < 4; i++)
            {
                var cell = CreateText(template.transform, "Cell" + i, "-", 18f, Palette.UiDark, 0.5f, false);
                cell.alignment = i >= 2 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                PlaceCorner(cell.rectTransform, new Vector2(0f, 0.5f), new Vector2(cx[i], 0f), new Vector2(cw[i], 44f));
            }
            template.gameObject.SetActive(false);
            var empty = TextAt(home, "HistoryEmpty", HX, -20f, HW, 60f, 22f, TextAlignmentOptions.Center, Palette.WithAlpha(Palette.UiLight, 0.6f), false);
            empty.text = "NO RUNS YET";

            // ---- results
            var results = CreateStretched("Results", tab);

            // Headline: two big numbers, each with its label, rating word, caption and (when comparing) the change.
            TextMeshProUGUI avgNumber = TextAt(results, "AvgNumber", L, Top, 250f, 112f, 96f, TextAlignmentOptions.MidlineLeft, Palette.UiLight, false, true);
            TextMeshProUGUI avgInfo = TextAt(results, "AvgInfo", L + 262f, Top - 6f, 440f, 70f, 28f, TextAlignmentOptions.TopLeft, Palette.UiLight, false, true);
            avgInfo.lineSpacing = 4f;
            TextMeshProUGUI avgDelta = TextAt(results, "AvgDelta", L + 262f, Top - 80f, 440f, 32f, 22f, TextAlignmentOptions.TopLeft, Palette.UiLight, false, true);
            TextMeshProUGUI lowNumber = TextAt(results, "LowNumber", 30f, Top, 250f, 112f, 96f, TextAlignmentOptions.MidlineLeft, Palette.UiLight, false, true);
            TextMeshProUGUI lowInfo = TextAt(results, "LowInfo", 292f, Top - 6f, 438f, 70f, 28f, TextAlignmentOptions.TopLeft, Palette.UiLight, false, true);
            lowInfo.lineSpacing = 4f;
            TextMeshProUGUI lowDelta = TextAt(results, "LowDelta", 292f, Top - 80f, 438f, 32f, 22f, TextAlignmentOptions.TopLeft, Palette.UiLight, false, true);

            // Summary: verdict box, graph, caption.
            var summary = CreateStretched("Summary", results);
            var verdictBg = CreateImage(summary, "VerdictBox", s_RoundedRect, Palette.WithAlpha(Palette.Shadow, 0.55f), true);
            Place(verdictBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1460f, 84f));
            var verdict = TextAt(summary, "Verdict", L + 24f, 132f, 1412f, 76f, 25f, TextAlignmentOptions.Center, Palette.UiLight);
            var graphBg = CreateImage(summary, "GraphBackground", s_RoundedRect, Palette.WithAlpha(Palette.InteriorDark, 0.85f), true);
            Place(graphBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(1460f, 250f));
            var graphRt = NewRect("Graph", graphBg.rectTransform);
            SetAnchors(graphRt, Vector2.zero, Vector2.one);
            graphRt.offsetMin = new Vector2(14f, 14f); graphRt.offsetMax = new Vector2(-104f, -14f);
            graphRt.gameObject.AddComponent<CanvasRenderer>();
            var graph = graphRt.gameObject.AddComponent<GraphicsGraph>();
            graph.raycastTarget = false;
            RectTransform GraphLabel(string text, Color col)
            {
                var t = CreateText(graphBg.rectTransform, "Label" + text.Replace(" ", ""), text, 20, col, 1f, false);
                t.alignment = TextAlignmentOptions.MidlineLeft;
                PlaceCorner(t.rectTransform, new Vector2(1f, 0f), new Vector2(-8f, 14f), new Vector2(92f, 26f));
                t.rectTransform.pivot = new Vector2(1f, 0.5f);
                return t.rectTransform;
            }
            var label60 = GraphLabel("60 FPS", Palette.Danger);
            var label120 = GraphLabel("120 FPS", Palette.CharacterPrimary);
            var caption = TextAt(summary, "Caption", L, -222f, 1460f, 30f, 20f, TextAlignmentOptions.Center, dim);

            // Details: three headed columns.
            var details = CreateStretched("Details", results);
            var timesText = TextAt(details, "FrameTimes", L, 132f, 380f, 386f, 18f, TextAlignmentOptions.TopLeft, Palette.UiLight);
            var systemText = TextAt(details, "System", -330f, 132f, 480f, 386f, 16f, TextAlignmentOptions.TopLeft, Palette.UiLight);
            var settingsA = TextAt(details, "SettingsA", 170f, 132f, 270f, 386f, 18f, TextAlignmentOptions.TopLeft, Palette.UiLight);
            var settingsB = TextAt(details, "SettingsB", 450f, 132f, 270f, 386f, 18f, TextAlignmentOptions.TopLeft, Palette.UiLight);
            foreach (var t in new[] { timesText, systemText, settingsA, settingsB }) t.lineSpacing = 3f;
            details.gameObject.SetActive(false);

            // One row of buttons.
            float bx = -690f, by = -288f;
            var againBtn = TabButton(results, "RunAgainButton", "RUN AGAIN", ref bx, 280f, by, 26f);
            var cols = againBtn.colors; cols.normalColor = Palette.Accent; cols.highlightedColor = Color.Lerp(Palette.Accent, Palette.UiLight, 0.35f);
            cols.selectedColor = Color.Lerp(Palette.Accent, Palette.UiLight, 0.35f); againBtn.colors = cols;
            var compareBtn = TabButton(results, "CompareButton", "COMPARE", ref bx, 400f, by, 22f);
            var detailsBtn = TabButton(results, "DetailsButton", "DETAILS", ref bx, 220f, by, 24f);
            var deleteBtn = TabButton(results, "DeleteButton", "DELETE", ref bx, 200f, by, 24f);
            var backBtn = TabButton(results, "BackToBenchmarkButton", "BACK", ref bx, 200f, by, 24f);

            var so = new SerializedObject(panel);
            Wire(so, "home", home.gameObject);
            Wire(so, "presetText", presetText);
            Wire(so, "displayText", displayText);
            Wire(so, "changedText", changed);
            Wire(so, "noteText", note);
            Wire(so, "runButton", run);
            Wire(so, "historyContent", histContent);
            Wire(so, "historyTemplate", template);
            Wire(so, "historyEmptyText", empty);
            Wire(so, "results", results.gameObject);
            Wire(so, "avgNumber", avgNumber);
            Wire(so, "avgInfo", avgInfo);
            Wire(so, "avgDelta", avgDelta);
            Wire(so, "lowNumber", lowNumber);
            Wire(so, "lowInfo", lowInfo);
            Wire(so, "lowDelta", lowDelta);
            Wire(so, "summaryGroup", summary.gameObject);
            Wire(so, "detailsGroup", details.gameObject);
            Wire(so, "verdictText", verdict);
            Wire(so, "captionText", caption);
            Wire(so, "graph", graph);
            Wire(so, "graphLabel60", label60);
            Wire(so, "graphLabel120", label120);
            Wire(so, "timesText", timesText);
            Wire(so, "systemText", systemText);
            Wire(so, "settingsTextA", settingsA);
            Wire(so, "settingsTextB", settingsB);
            Wire(so, "compareButton", compareBtn);
            Wire(so, "compareButtonLabel", compareBtn.GetComponentInChildren<TextMeshProUGUI>());
            Wire(so, "againButton", againBtn);
            Wire(so, "detailsButton", detailsBtn);
            Wire(so, "detailsButtonLabel", detailsBtn.GetComponentInChildren<TextMeshProUGUI>());
            Wire(so, "deleteButton", deleteBtn);
            Wire(so, "backButton", backBtn);
            so.ApplyModifiedPropertiesWithoutUndo();

            results.gameObject.SetActive(false);
            runButton = run;
            return panel;
        }

        // ------------------------------------------------------------------ rows

        // pos is ignored (default) when the row sits in a layout group: the group sizes and places it.
        static UnityEngine.UI.Image CreateRowBase(RectTransform parent, string name, string label, Vector2 pos,
            float width, float height, bool raycast, float labelFrac = 0.34f, bool inLayout = false)
        {
            var bg = CreateImage(parent, name, s_RoundedRect, default, true);
            bg.raycastTarget = raycast;
            if (inLayout)
            {
                var le = bg.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                le.preferredWidth = width; le.preferredHeight = height;
            }
            else Place(bg.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(width, height));

            var text = CreateText(bg.rectTransform, "Label", label, 28, Palette.UiLight, 4f, true);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceCorner(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(width * labelFrac, height));
            return bg;
        }

        // A row that is itself selectable: faint panel, accent tint when selected.
        static void StyleRowSelectable(UnityEngine.UI.Selectable sel, UnityEngine.UI.Image bg)
        {
            bg.color = Color.white;
            sel.targetGraphic = bg;
            sel.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = sel.colors;
            colors.normalColor = Palette.WithAlpha(Palette.Shadow, 0.4f);
            colors.highlightedColor = Palette.WithAlpha(Palette.UiLight, 0.18f);
            colors.selectedColor = Palette.WithAlpha(Palette.Accent, 0.4f);
            colors.pressedColor = Palette.WithAlpha(Palette.Accent, 0.55f);
            colors.disabledColor = Palette.WithAlpha(Palette.Shadow, 0.3f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            sel.colors = colors;
        }

        static UnityEngine.UI.Selectable AddSliderRow(RectTransform parent, SettingId id, string label, Vector2 pos,
            float width, float height, bool inLayout = false, string description = null)
        {
            var bg = CreateRowBase(parent, id + "Row", label, pos, width, height, inLayout, 0.34f, inLayout);
            bg.color = Palette.WithAlpha(Palette.Shadow, 0.4f);
            var row = bg.rectTransform;

            const float valueW = 150f;
            var slider = CreateSlider(row, "Slider", Vector2.zero);
            float sx = 22f + width * 0.34f + 14f;
            PlaceCorner((RectTransform)slider.transform, new Vector2(0f, 0.5f), new Vector2(sx, 0f),
                new Vector2(width - sx - valueW - 34f, 40f));

            var value = CreateText(row, "Value", "0", 28, Palette.Accent, 2f, true);
            value.alignment = TextAlignmentOptions.MidlineRight;
            PlaceCorner(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(valueW, height));

            var comp = bg.gameObject.AddComponent<SettingsSliderRow>();
            var so = new SerializedObject(comp);
            so.FindProperty("id").enumValueIndex = (int)id;
            Wire(so, "slider", slider);
            Wire(so, "valueText", value);
            so.ApplyModifiedPropertiesWithoutUndo();
            AddDescription(slider.gameObject, description);
            AddDescription(bg.gameObject, description);
            return slider;
        }

        static UnityEngine.UI.Selectable AddStepperRow(RectTransform parent, SettingId id, string label, Vector2 pos,
            float width, float height, bool withSwatch, float valueWidth = 260f, bool inLayout = false, string description = null)
        {
            var bg = CreateRowBase(parent, id + "Row", label, pos, width, height, true,
                inLayout ? Mathf.Min(0.4f, (width - valueWidth - 190f) / width) : 0.34f, inLayout);
            var row = bg.rectTransform;
            var comp = bg.gameObject.AddComponent<SettingsStepperRow>();
            StyleRowSelectable(comp, bg);

            var next = CreateArrowButton(row, "Next", ">", new Vector2(-20f, 0f));
            var value = CreateText(row, "Value", "-", 30, Palette.UiLight, 4f, true);
            if (valueWidth > 300f) { value.fontSize = 26f; value.characterSpacing = 1f; }
            PlaceCorner(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-84f, 0f), new Vector2(valueWidth, height));
            var prev = CreateArrowButton(row, "Prev", "<", new Vector2(-(92f + valueWidth), 0f));

            UnityEngine.UI.Image swatch = null;
            if (withSwatch)
            {
                swatch = CreateImage(row, "Swatch", s_RoundedRect, Palette.UiLight, true);
                PlaceCorner(swatch.rectTransform, new Vector2(1f, 0.5f), new Vector2(-(158f + valueWidth), 0f), new Vector2(36f, 36f));
            }

            var so = new SerializedObject(comp);
            so.FindProperty("id").enumValueIndex = (int)id;
            Wire(so, "valueText", value);
            Wire(so, "swatch", swatch);
            Wire(so, "prevButton", prev);
            Wire(so, "nextButton", next);
            so.ApplyModifiedPropertiesWithoutUndo();
            AddDescription(bg.gameObject, description);
            return comp;
        }

        static UnityEngine.UI.Selectable AddToggleRow(RectTransform parent, SettingId id, string label, Vector2 pos,
            float width, float height, float labelFrac = 0.34f, bool inLayout = false, string description = null)
        {
            var bg = CreateRowBase(parent, id + "Row", label, pos, width, height, true, labelFrac, inLayout);
            var row = bg.rectTransform;
            var comp = bg.gameObject.AddComponent<SettingsToggleRow>();
            StyleRowSelectable(comp, bg);

            var pill = CreateImage(row, "Pill", s_RoundedRect, Palette.Accent, true);
            PlaceCorner(pill.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(120f, Mathf.Min(44f, height - 8f)));
            var value = CreateText(pill.rectTransform, "Value", "ON", 26, Palette.UiDark, 4f, false);
            Stretch(value.rectTransform);

            var so = new SerializedObject(comp);
            so.FindProperty("id").enumValueIndex = (int)id;
            Wire(so, "valueText", value);
            Wire(so, "pill", pill);
            so.ApplyModifiedPropertiesWithoutUndo();
            AddDescription(bg.gameObject, description);
            return comp;
        }

        // Small mouse-clickable arrow; skipped by gamepad navigation (the row handles left and right).
        static UnityEngine.UI.Button CreateArrowButton(RectTransform parent, string name, string glyph, Vector2 pos)
        {
            var button = CreateButton(parent, name, glyph, Vector2.zero, new Vector2(56f, 44f), 32f);
            PlaceCorner((RectTransform)button.transform, new Vector2(1f, 0.5f), pos, new Vector2(56f, 44f));
            var nav = button.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            button.navigation = nav;
            return button;
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
            Vector2 pos, Vector2? size = null, float fontSize = 44f)
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
            var text = CreateText(image.rectTransform, "Label", label, fontSize, Palette.UiDark, 4f, false);
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

        static Sprite GetOrCreateScrim()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(ScrimPath);
            if (existing != null) return existing;
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float t = 1f - (y + 0.5f) / size;
                float a = 1f - Mathf.Pow(t, 1.6f);
                for (int x = 0; x < size; x++) pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            return WriteSprite(ScrimPath, size, pixels, Vector4.zero);
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
