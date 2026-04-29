using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WTTDP.AccessibilityMod;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class AccessibilityPlugin : BaseUnityPlugin
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private ConfigEntry<KeyboardShortcut> _repeatStateKey = null!;
    private ConfigEntry<KeyboardShortcut> _toggleAutoChoicesKey = null!;
    private ConfigEntry<KeyboardShortcut> _announceControlsKey = null!;
    private ConfigEntry<bool> _announceTutorials = null!;
    private ConfigEntry<bool> _announcePocketChanges = null!;
    private ConfigEntry<bool> _announceFocusChanges = null!;
    private ConfigEntry<bool> _announceChoicesAfterStory = null!;
    private ConfigEntry<bool> _announceVisualScenes = null!;

    private Type? _roomManagerType;
    private Type? _textHoverType;
    private Component? _roomManager;
    private string _lastSceneName = string.Empty;
    private object? _lastNode;
    private string _lastNodeSpeech = string.Empty;
    private string _lastNodeMainText = string.Empty;
    private string _lastFocusSpeech = string.Empty;
    private string _lastFocusToken = string.Empty;
    private string _lastTutorialSpeech = string.Empty;
    private string _lastPocketSpeech = string.Empty;
    private bool _lastMapOpen;
    private bool _lastTapeRecorderOpen;
    private float _nextManagerSearchTime;
    private float _nextFocusScanTime;
    private bool _loggedMissingNvda;
    private FocusState _currentFocusState = FocusState.Empty;
    private bool _awaitingManualFocusAnnouncement;
    private bool _choiceAnnouncementPending;
    private float _choiceAnnouncementReadyTime;
    private float _choiceAnnouncementExpireTime;
    private string _lastChoiceSummaryAnnounced = string.Empty;
    private float _lastHorizontalAxis;
    private float _lastVerticalAxis;
    private float _storyNodeActiveSince = -1f;
    private bool _pendingPocketToggleAnnouncement;
    private float _pendingPocketToggleAnnouncementTime;
    private bool _pocketModeOpen;
    private bool _pocketModeInitialized;

    private void Awake()
    {
        _repeatStateKey = Config.Bind("Hotkeys", "RepeatState", new KeyboardShortcut(KeyCode.F6), "Repeats the current room text and any focused choice.");
        _toggleAutoChoicesKey = Config.Bind("Hotkeys", "ToggleAutoChoiceReadback", new KeyboardShortcut(KeyCode.F8), "Turns automatic choice readback after story text on or off.");
        _announceControlsKey = Config.Bind("Hotkeys", "AnnounceControls", new KeyboardShortcut(KeyCode.F5), "Reads the main game and mod controls.");
        _announceTutorials = Config.Bind("Speech", "AnnounceTutorialText", true, "Speaks tutorial or hint text when it changes.");
        _announcePocketChanges = Config.Bind("Speech", "AnnouncePocketChanges", true, "Speaks pocket or inventory text when it changes.");
        _announceFocusChanges = Config.Bind("Speech", "AnnounceFocusChanges", true, "Speaks focused menu buttons and keyboard-selected choices.");
        _announceChoicesAfterStory = Config.Bind("Speech", "AnnounceChoicesAfterStory", true, "Reads the currently available live choices once after the main story text.");
        _announceVisualScenes = Config.Bind("Speech", "AnnounceVisualScenes", true, "Speaks brief curated descriptions for visual-only scenes when they begin.");

        _roomManagerType = ResolveType("RoomManager");
        _textHoverType = ResolveType("TextHover");

        if (_roomManagerType == null)
        {
            Logger.LogWarning("RoomManager type was not found. The mod will keep scanning while the game is running.");
        }

        if (!NvdaControllerClient.IsAvailable())
        {
            _loggedMissingNvda = true;
            Logger.LogWarning("NVDA is not currently reachable. Start NVDA and keep nvdaControllerClient32/64.dll beside the game executable.");
        }
    }

    private void Update()
    {
        HandleNumericChoiceSelection();

        if (_repeatStateKey.Value.IsDown())
        {
            RepeatCurrentState();
        }

        if (_toggleAutoChoicesKey.Value.IsDown())
        {
            ToggleAutoChoiceReadback();
        }

        if (_announceControlsKey.Value.IsDown())
        {
            AnnounceControls();
        }

        RefreshRoomManager();
        AnnounceSceneChanges();

        if (_roomManager == null)
        {
            return;
        }

        TrackStoryNodeState();
        TrackPocketToggleRequest();
        AnnounceNodeChanges();
        AnnounceChoicesWhenReady();
        AnnounceFocusChanges();
        AnnounceTutorialText();
        AnnouncePocketText();
        AnnounceModeChanges();

        if (!_loggedMissingNvda && !NvdaControllerClient.IsAvailable())
        {
            _loggedMissingNvda = true;
            Logger.LogWarning("NVDA became unavailable. Speech output will resume automatically if NVDA is restarted.");
        }
        else if (_loggedMissingNvda && NvdaControllerClient.IsAvailable())
        {
            _loggedMissingNvda = false;
            Logger.LogInfo("NVDA is available again.");
        }
    }

    private void HandleNumericChoiceSelection()
    {
        if (_roomManager == null)
        {
            return;
        }

        var requestedChoice = GetRequestedNumericChoice();
        if (requestedChoice == 0)
        {
            return;
        }

        var buttons = GetOrderedVisibleButtons().ToList();
        if (requestedChoice > buttons.Count)
        {
            Speak($"Choice {requestedChoice} is not available here.", interrupt: true);
            return;
        }

        var button = buttons[requestedChoice - 1];
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        button.onClick.Invoke();
    }

    private static int GetRequestedNumericChoice()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            return 1;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            return 2;
        }

        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            return 3;
        }

        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            return 4;
        }

        return 0;
    }

    private void TrackStoryNodeState()
    {
        if (GetFieldValue<object>(_roomManager, "currentNode") != null)
        {
            if (_storyNodeActiveSince < 0f)
            {
                _storyNodeActiveSince = Time.unscaledTime;
            }
        }
        else
        {
            _storyNodeActiveSince = -1f;
        }
    }

    private void TrackPocketToggleRequest()
    {
        if (!HasStableStoryNode() || IsLikelyMainMenu())
        {
            _pendingPocketToggleAnnouncement = false;
            _pocketModeOpen = false;
            _pocketModeInitialized = false;
            return;
        }

        if (!_pocketModeInitialized)
        {
            _pocketModeOpen = false;
            _pocketModeInitialized = true;
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            _pendingPocketToggleAnnouncement = true;
            _pendingPocketToggleAnnouncementTime = Time.unscaledTime + 0.2f;
        }
    }

    private void RefreshRoomManager()
    {
        if (_roomManager != null)
        {
            return;
        }

        if (Time.unscaledTime < _nextManagerSearchTime)
        {
            return;
        }

        _nextManagerSearchTime = Time.unscaledTime + 1f;
        if (_roomManagerType == null)
        {
            _roomManagerType = ResolveType("RoomManager");
            if (_roomManagerType == null)
            {
                return;
            }
        }

        _roomManager = FindComponentByType(_roomManagerType);
        if (_roomManager != null)
        {
            Logger.LogInfo("Attached to RoomManager.");
        }
    }

    private void AnnounceSceneChanges()
    {
        var sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
        if (sceneName == _lastSceneName)
        {
            return;
        }

        _lastSceneName = sceneName;
    }

    private void AnnounceNodeChanges()
    {
        var node = GetFieldValue<object>(_roomManager, "currentNode");
        if (ReferenceEquals(node, _lastNode) || node == null)
        {
            return;
        }

        var previousNodeMainText = _lastNodeMainText;
        var selectedChoiceAtTransition = GetSelectionSpeechForTransition();
        _lastNode = node;
        _lastNodeSpeech = BuildNodeSpeech(node);
        _lastNodeMainText = Normalize(GetFieldValue<string>(node, "m_text"));
        _lastFocusSpeech = string.Empty;
        _lastFocusToken = string.Empty;
        _currentFocusState = FocusState.Empty;
        _awaitingManualFocusAnnouncement = true;
        _choiceAnnouncementPending = _announceChoicesAfterStory.Value;
        _choiceAnnouncementReadyTime = Time.unscaledTime + EstimateStorySpeechSeconds(node);
        _choiceAnnouncementExpireTime = _choiceAnnouncementReadyTime + 10f;
        _lastChoiceSummaryAnnounced = string.Empty;
        var visualSceneDescription = GetVisualSceneDescription(previousNodeMainText, selectedChoiceAtTransition);
        if (string.IsNullOrWhiteSpace(visualSceneDescription))
        {
            Speak(_lastNodeSpeech, interrupt: true);
            return;
        }

        var combinedSpeech = string.IsNullOrWhiteSpace(_lastNodeSpeech)
            ? visualSceneDescription
            : $"{visualSceneDescription} {_lastNodeSpeech}";
        Speak(combinedSpeech, interrupt: true);
    }

    private void ToggleAutoChoiceReadback()
    {
        _announceChoicesAfterStory.Value = !_announceChoicesAfterStory.Value;
        if (_announceChoicesAfterStory.Value)
        {
            _choiceAnnouncementPending = true;
            _choiceAnnouncementReadyTime = Time.unscaledTime;
            _choiceAnnouncementExpireTime = Time.unscaledTime + 10f;
            _lastChoiceSummaryAnnounced = string.Empty;
        }
        else
        {
            _choiceAnnouncementPending = false;
        }

        var state = _announceChoicesAfterStory.Value ? "on" : "off";
        Speak($"Automatic choice readback {state}.", interrupt: true);
    }

    private void AnnounceChoicesWhenReady()
    {
        if (!_choiceAnnouncementPending || Time.unscaledTime < _choiceAnnouncementReadyTime)
        {
            return;
        }

        if (Time.unscaledTime > _choiceAnnouncementExpireTime)
        {
            _choiceAnnouncementPending = false;
            return;
        }

        var availableChoicesSummary = GetAvailableChoicesSummary();
        if (string.IsNullOrWhiteSpace(availableChoicesSummary))
        {
            return;
        }

        if (availableChoicesSummary == _lastChoiceSummaryAnnounced)
        {
            _choiceAnnouncementPending = false;
            return;
        }

        _lastChoiceSummaryAnnounced = availableChoicesSummary;
        _choiceAnnouncementPending = false;
        Speak(availableChoicesSummary, interrupt: false);
    }

    private void AnnounceFocusChanges()
    {
        if (!_announceFocusChanges.Value || Time.unscaledTime < _nextFocusScanTime)
        {
            return;
        }

        _nextFocusScanTime = Time.unscaledTime + 0.1f;
        var focusState = GetFocusedElementState();
        if (string.IsNullOrWhiteSpace(focusState.Speech))
        {
            return;
        }

        if (focusState.Token != _currentFocusState.Token)
        {
            _currentFocusState = focusState;
        }

        if (_awaitingManualFocusAnnouncement && !DidManualChoiceNavigationOccur())
        {
            return;
        }

        if (focusState.Token == _lastFocusToken)
        {
            return;
        }

        _awaitingManualFocusAnnouncement = false;
        _lastFocusToken = focusState.Token;
        _lastFocusSpeech = focusState.Speech;
        Speak(focusState.Speech, interrupt: true);
    }

    private void AnnounceTutorialText()
    {
        if (!_announceTutorials.Value || !HasStableStoryNode() || IsLikelyMainMenu() || !IsTutorialUiVisible())
        {
            _lastTutorialSpeech = string.Empty;
            return;
        }

        var tutorialText = GetGraphicText(GetFieldValue<object>(_roomManager, "tutorialText"));
        if (string.IsNullOrWhiteSpace(tutorialText) || tutorialText == _lastTutorialSpeech)
        {
            return;
        }

        if (ShouldSuppressTutorialText(tutorialText))
        {
            _lastTutorialSpeech = tutorialText;
            return;
        }

        _lastTutorialSpeech = tutorialText;
        Speak($"Hint. {tutorialText}", interrupt: true);
    }

    private void AnnouncePocketText()
    {
        if (!_announcePocketChanges.Value || !HasStableStoryNode() || IsLikelyMainMenu())
        {
            _lastPocketSpeech = string.Empty;
            _pendingPocketToggleAnnouncement = false;
            _pocketModeOpen = false;
            _pocketModeInitialized = false;
            return;
        }

        if (!_pendingPocketToggleAnnouncement || Time.unscaledTime < _pendingPocketToggleAnnouncementTime)
        {
            return;
        }

        _pendingPocketToggleAnnouncement = false;
        _pocketModeOpen = !_pocketModeOpen;

        if (!_pocketModeOpen)
        {
            Speak("Pockets closed.", interrupt: true);
            _lastPocketSpeech = string.Empty;
            return;
        }

        var pocketText = GetPocketSummary();
        _lastPocketSpeech = pocketText;

        if (string.IsNullOrWhiteSpace(pocketText))
        {
            Speak("Pockets opened. Empty.", interrupt: true);
            return;
        }

        Speak($"Pockets opened. {pocketText}", interrupt: true);
    }

    private void AnnounceModeChanges()
    {
        var mapOpen = GetFieldValue<bool>(_roomManager, "mapOpen");
        if (mapOpen != _lastMapOpen)
        {
            _lastMapOpen = mapOpen;
            Speak(mapOpen ? "Map opened" : "Map closed", interrupt: true);
        }

        var tapeRecorderOpen = GetFieldValue<bool>(_roomManager, "tapeRecorderOpen");
        if (tapeRecorderOpen != _lastTapeRecorderOpen)
        {
            _lastTapeRecorderOpen = tapeRecorderOpen;
            Speak(tapeRecorderOpen ? "Tape recorder opened" : "Tape recorder closed", interrupt: true);
        }
    }

    private void RepeatCurrentState()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_lastNodeSpeech))
        {
            parts.Add(_lastNodeSpeech);
        }

        var availableChoicesSummary = GetAvailableChoicesSummary();
        if (!string.IsNullOrWhiteSpace(availableChoicesSummary))
        {
            parts.Add(availableChoicesSummary);
        }

        if (!string.IsNullOrWhiteSpace(_lastFocusSpeech))
        {
            parts.Add($"Focused. {_lastFocusSpeech}");
        }

        if (parts.Count == 0)
        {
            parts.Add("No room state has been captured yet.");
        }

        Speak(string.Join(" ", parts), interrupt: true);
    }

    private void AnnounceControls()
    {
        const string controls =
            "Controls. Up and down arrow, or W and S, move between choices. Enter selects a choice. " +
            "1 through 4 activate the visible choices from top to bottom. " +
            "F5 repeats these controls. F6 rereads the current text and choices. " +
            "F8 toggles automatic choice readback. C opens the map, which is not accessible for blind players. " +
            "X opens or closes pockets to review what you have. Hold Escape to return to the main menu. " +
            "If the journal mod is installed, F7 opens the journal, F9 pastes the current body text into the journal, and F10 rereads the current journal state.";

        Speak(controls, interrupt: true);
    }

    private FocusState GetFocusedElementState()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        var selectedState = GetGameObjectFocusState(selected);
        if (!string.IsNullOrWhiteSpace(selectedState.Speech))
        {
            return selectedState;
        }

        if (_textHoverType == null)
        {
            _textHoverType = ResolveType("TextHover");
            if (_textHoverType == null)
            {
                return FocusState.Empty;
            }
        }

        foreach (var obj in Resources.FindObjectsOfTypeAll(_textHoverType))
        {
            if (obj is not Component component)
            {
                continue;
            }

            var isSelected = GetFieldValue<bool>(component, "thisButtonIsSelected") || GetFieldValue<bool>(component, "BeingHovered");
            if (!isSelected)
            {
                continue;
            }

            var textObject = GetFieldValue<object>(component, "thisText");
            var text = GetGraphicText(textObject);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var choiceNumber = GetFieldValue<int>(component, "thisChoiceNumber");
                var token = $"texthover:{choiceNumber}:{component.GetInstanceID()}";
                return new FocusState(text, token);
            }
        }

        return FocusState.Empty;
    }

    private string BuildNodeSpeech(object node)
    {
        var parts = new List<string>();

        var mainText = Normalize(GetFieldValue<string>(node, "m_text"));
        if (!string.IsNullOrWhiteSpace(mainText))
        {
            parts.Add(mainText);
        }

        var itemText = Normalize(GetFieldValue<string>(node, "ItemString"));
        if (!string.IsNullOrWhiteSpace(itemText))
        {
            parts.Add($"Item. {itemText}.");
        }

        var removedItemText = Normalize(GetFieldValue<string>(node, "RemoveItem"));
        if (!string.IsNullOrWhiteSpace(removedItemText))
        {
            parts.Add($"Removed item. {removedItemText}.");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string GetSelectionSpeechForTransition()
    {
        var focusState = GetFocusedElementState();
        if (!string.IsNullOrWhiteSpace(focusState.Speech))
        {
            return focusState.Speech;
        }

        return _lastFocusSpeech;
    }

    private string GetVisualSceneDescription(string previousNodeMainText, string selectedChoiceText)
    {
        if (!_announceVisualScenes.Value)
        {
            return string.Empty;
        }

        var previousText = Normalize(previousNodeMainText).ToUpperInvariant();
        var choiceText = Normalize(selectedChoiceText).ToUpperInvariant();

        if (previousText.StartsWith("HOLE.") && choiceText == "JUMP IN.")
        {
            return "You jump into the hole and fall into darkness.";
        }

        return string.Empty;
    }

    private static float EstimateStorySpeechSeconds(object node)
    {
        var mainText = Normalize(GetFieldValue<string>(node, "m_text"));
        var itemText = Normalize(GetFieldValue<string>(node, "ItemString"));
        var removedItemText = Normalize(GetFieldValue<string>(node, "RemoveItem"));

        var storyOnly = string.Join(" ", new[] { mainText, itemText, removedItemText }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(storyOnly))
        {
            return 0.5f;
        }

        var wordCount = storyOnly.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Mathf.Clamp(wordCount / 3.2f, 0.75f, 20f);
    }

    private string GetAvailableChoicesSummary()
    {
        var choices = GetActiveChoiceTexts().ToList();
        if (choices.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("Choices.");
        for (var i = 0; i < choices.Count; i++)
        {
            builder.Append(' ');
            builder.Append(i + 1);
            builder.Append(". ");
            builder.Append(choices[i]);
            builder.Append('.');
        }

        return builder.ToString();
    }

    private IEnumerable<string> GetActiveChoiceTexts()
    {
        var textHoverChoices = GetActiveTextHoverChoices().ToList();
        if (textHoverChoices.Count > 0)
        {
            foreach (var choice in textHoverChoices)
            {
                yield return choice;
            }

            yield break;
        }

        if (_roomManager == null)
        {
            yield break;
        }

        for (var i = 1; i <= 4; i++)
        {
            var button = GetFieldValue<Button>(_roomManager, $"m_button{i}");
            if (button == null || button.gameObject == null || !button.gameObject.activeInHierarchy || !button.isActiveAndEnabled)
            {
                continue;
            }

            var text = GetGameObjectFocusState(button.gameObject).Speech;
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    private IEnumerable<string> GetActiveTextHoverChoices()
    {
        if (_textHoverType == null)
        {
            _textHoverType = ResolveType("TextHover");
            if (_textHoverType == null)
            {
                yield break;
            }
        }

        var orderedChoices = new List<VisibleChoice>();
        foreach (var obj in Resources.FindObjectsOfTypeAll(_textHoverType))
        {
            if (obj is not Component component || component.gameObject == null || !component.gameObject.activeInHierarchy)
            {
                continue;
            }

            var textObject = GetFieldValue<object>(component, "thisText");
            var text = GetGraphicText(textObject);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (textObject is Graphic graphic && !graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            var rectTransform = component.GetComponent<RectTransform>() ?? (textObject as Component)?.GetComponent<RectTransform>();
            if (rectTransform != null && !IsRectTransformVisible(rectTransform))
            {
                continue;
            }

            var yPosition = rectTransform != null
                ? rectTransform.TransformPoint(rectTransform.rect.center).y
                : component.transform.position.y;
            var xPosition = rectTransform != null
                ? rectTransform.TransformPoint(rectTransform.rect.center).x
                : component.transform.position.x;
            var token = $"texthover:{component.GetInstanceID()}:{text}";

            if (orderedChoices.Any(choice => choice.Token == token))
            {
                continue;
            }

            orderedChoices.Add(new VisibleChoice(text, token, yPosition, xPosition));
        }

        foreach (var choice in orderedChoices
            .OrderByDescending(choice => choice.Y)
            .ThenBy(choice => choice.X))
        {
            yield return choice.Text;
        }
    }

    private bool DidManualChoiceNavigationOccur()
    {
        var horizontal = Input.GetAxisRaw("Horizontal");
        var vertical = Input.GetAxisRaw("Vertical");
        var horizontalEdge = Mathf.Abs(horizontal) > 0.5f && Mathf.Abs(_lastHorizontalAxis) <= 0.5f;
        var verticalEdge = Mathf.Abs(vertical) > 0.5f && Mathf.Abs(_lastVerticalAxis) <= 0.5f;
        _lastHorizontalAxis = horizontal;
        _lastVerticalAxis = vertical;

        return Input.GetKeyDown(KeyCode.UpArrow)
            || Input.GetKeyDown(KeyCode.DownArrow)
            || Input.GetKeyDown(KeyCode.LeftArrow)
            || Input.GetKeyDown(KeyCode.RightArrow)
            || Input.GetKeyDown(KeyCode.W)
            || Input.GetKeyDown(KeyCode.A)
            || Input.GetKeyDown(KeyCode.S)
            || Input.GetKeyDown(KeyCode.D)
            || verticalEdge
            || horizontalEdge;
    }

    private bool IsPocketUiVisible()
    {
        var pocketUi = GetFieldValue<GameObject>(_roomManager, "PocketUI");
        if (pocketUi == null || !pocketUi.activeInHierarchy)
        {
            return false;
        }

        var canvasGroup = pocketUi.GetComponent<CanvasGroup>();
        if (canvasGroup != null && canvasGroup.alpha <= 0.01f)
        {
            return false;
        }

        var rectTransform = pocketUi.GetComponent<RectTransform>();
        return rectTransform == null || IsRectTransformVisible(rectTransform);
    }

    private string GetPocketSummary()
    {
        var pocketUi = GetFieldValue<GameObject>(_roomManager, "PocketUI");
        if (pocketUi == null)
        {
            return string.Empty;
        }

        var visibleTexts = new List<VisibleChoice>();

        foreach (var text in pocketUi.GetComponentsInChildren<Text>(includeInactive: false))
        {
            AddVisiblePocketText(visibleTexts, text.text, text.rectTransform);
        }

        foreach (var tmpText in pocketUi.GetComponentsInChildren<TMP_Text>(includeInactive: false))
        {
            AddVisiblePocketText(visibleTexts, tmpText.text, tmpText.rectTransform);
        }

        var filtered = visibleTexts
            .OrderByDescending(choice => choice.Y)
            .ThenBy(choice => choice.X)
            .Select(choice => Normalize(choice.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Where(text => !text.Equals("POCKETS", StringComparison.OrdinalIgnoreCase))
            .Where(text => !text.Equals("N", StringComparison.OrdinalIgnoreCase))
            .Where(text => !text.Equals("E", StringComparison.OrdinalIgnoreCase))
            .Where(text => !text.Equals("S", StringComparison.OrdinalIgnoreCase))
            .Where(text => !text.Equals("W", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        return string.Join(". ", filtered);
    }

    private IEnumerable<Button> GetOrderedVisibleButtons()
    {
        if (_roomManager == null)
        {
            yield break;
        }

        var orderedButtons = new List<(Button Button, float Y, float X)>();
        for (var i = 1; i <= 4; i++)
        {
            var button = GetFieldValue<Button>(_roomManager, $"m_button{i}");
            if (button == null || button.gameObject == null || !button.gameObject.activeInHierarchy || !button.isActiveAndEnabled || !button.interactable)
            {
                continue;
            }

            var rectTransform = button.GetComponent<RectTransform>();
            if (rectTransform != null && !IsRectTransformVisible(rectTransform))
            {
                continue;
            }

            var yPosition = rectTransform != null
                ? rectTransform.TransformPoint(rectTransform.rect.center).y
                : button.transform.position.y;
            var xPosition = rectTransform != null
                ? rectTransform.TransformPoint(rectTransform.rect.center).x
                : button.transform.position.x;
            orderedButtons.Add((button, yPosition, xPosition));
        }

        foreach (var entry in orderedButtons
            .OrderByDescending(entry => entry.Y)
            .ThenBy(entry => entry.X))
        {
            yield return entry.Button;
        }
    }

    private static bool ShouldSuppressTutorialText(string tutorialText)
    {
        var normalized = Normalize(tutorialText).ToUpperInvariant();
        return normalized.Contains("PRESS [X] TO CHECK YOUR POCKETS");
    }

    private static void AddVisiblePocketText(List<VisibleChoice> visibleTexts, string rawText, RectTransform rectTransform)
    {
        var text = Normalize(rawText);
        if (string.IsNullOrWhiteSpace(text) || rectTransform == null || !IsRectTransformVisible(rectTransform))
        {
            return;
        }

        var center = rectTransform.TransformPoint(rectTransform.rect.center);
        visibleTexts.Add(new VisibleChoice(text, $"pocket:{rectTransform.GetInstanceID()}:{text}", center.y, center.x));
    }

    private bool HasActiveStoryNode()
    {
        return GetFieldValue<object>(_roomManager, "currentNode") != null;
    }

    private bool HasStableStoryNode()
    {
        return HasActiveStoryNode() && _storyNodeActiveSince >= 0f && Time.unscaledTime - _storyNodeActiveSince >= 0.5f;
    }

    private bool IsLikelyMainMenu()
    {
        var choices = GetActiveChoiceTexts().ToList();
        if (choices.Count == 0)
        {
            return false;
        }

        var normalized = choices
            .Select(choice => Normalize(choice).ToUpperInvariant())
            .ToList();

        return normalized.Any(choice => choice.StartsWith("START"))
            && normalized.Any(choice => choice.StartsWith("OPTIONS"));
    }

    private bool IsTutorialUiVisible()
    {
        var tutorialTextObject = GetFieldValue<object>(_roomManager, "tutorialText");
        if (tutorialTextObject is not Graphic graphic || graphic.gameObject == null || !graphic.gameObject.activeInHierarchy)
        {
            return false;
        }

        var canvasGroup = graphic.GetComponentInParent<CanvasGroup>();
        if (canvasGroup != null && (canvasGroup.alpha <= 0.01f || !canvasGroup.interactable))
        {
            return false;
        }

        var rectTransform = graphic.rectTransform;
        return rectTransform != null && IsRectTransformVisible(rectTransform);
    }

    private static bool IsRectTransformVisible(RectTransform rectTransform)
    {
        if (rectTransform.rect.width <= 0f || rectTransform.rect.height <= 0f)
        {
            return false;
        }

        var lossyScale = rectTransform.lossyScale;
        return Mathf.Abs(lossyScale.x) > 0.001f && Mathf.Abs(lossyScale.y) > 0.001f;
    }

    private static FocusState GetGameObjectFocusState(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return FocusState.Empty;
        }

        var text = gameObject.GetComponent<Text>();
        if (text != null)
        {
            return BuildFocusState(Normalize(text.text), gameObject);
        }

        var tmpText = gameObject.GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            return BuildFocusState(Normalize(tmpText.text), gameObject);
        }

        text = gameObject.GetComponentInChildren<Text>(includeInactive: true);
        if (text != null)
        {
            return BuildFocusState(Normalize(text.text), gameObject);
        }

        tmpText = gameObject.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (tmpText != null)
        {
            return BuildFocusState(Normalize(tmpText.text), gameObject);
        }

        return BuildFocusState(Normalize(gameObject.name), gameObject);
    }

    private static FocusState BuildFocusState(string speech, GameObject gameObject)
    {
        if (string.IsNullOrWhiteSpace(speech))
        {
            return FocusState.Empty;
        }

        return new FocusState(speech, $"eventsystem:{gameObject.GetInstanceID()}");
    }

    private static string GetGraphicText(object? graphic)
    {
        if (graphic is Text text)
        {
            return Normalize(text.text);
        }

        if (graphic is TMP_Text tmpText)
        {
            return Normalize(tmpText.text);
        }

        return string.Empty;
    }

    private void Speak(string? text, bool interrupt)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        NvdaControllerClient.TrySpeak(normalized, interrupt);
    }

    private static T GetFieldValue<T>(object? target, string fieldName)
    {
        if (target == null)
        {
            return default!;
        }

        var type = target.GetType();
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            return default!;
        }

        var value = field.GetValue(target);
        if (value is T typed)
        {
            return typed;
        }

        return default!;
    }

    private static Type? ResolveType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static Component? FindComponentByType(Type componentType)
    {
        var obj = UnityEngine.Object.FindObjectOfType(componentType);
        return obj as Component;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text!.Replace('\r', ' ').Replace('\n', ' ');
        cleaned = cleaned.Replace("<b>", string.Empty).Replace("</b>", string.Empty);
        cleaned = cleaned.Replace("<i>", string.Empty).Replace("</i>", string.Empty);
        cleaned = cleaned.Replace("<br>", " ").Replace("<br/>", " ").Replace("<br />", " ");
        cleaned = Regex.Replace(cleaned, "<.*?>", string.Empty);
        cleaned = WhitespaceRegex.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    private struct FocusState
    {
        public FocusState(string speech, string token)
        {
            Speech = speech;
            Token = token;
        }

        public string Speech { get; }

        public string Token { get; }

        public static FocusState Empty => new FocusState(string.Empty, string.Empty);
    }

    private struct VisibleChoice
    {
        public VisibleChoice(string text, string token, float y, float x)
        {
            Text = text;
            Token = token;
            Y = y;
            X = x;
        }

        public string Text { get; }

        public string Token { get; }

        public float Y { get; }

        public float X { get; }
    }
}
