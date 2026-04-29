using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTTDP.JournalMod;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class JournalPlugin : BaseUnityPlugin
{
    private static JournalPlugin? s_instance;
    private static bool s_overlayOpen;

    private ConfigEntry<KeyboardShortcut> _toggleJournalKey = null!;
    private ConfigEntry<KeyboardShortcut> _repeatJournalKey = null!;
    private ConfigEntry<KeyboardShortcut> _pasteSceneTextKey = null!;
    private Harmony? _harmony;
    private readonly GUIStyle _windowStyle = new();
    private readonly GUIStyle _bodyStyle = new();
    private readonly GUIStyle _selectedStyle = new();
    private readonly GUIStyle _normalStyle = new();

    private readonly List<JournalPage> _pages = new();
    private readonly BindingFlags _fieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private string _savePath = string.Empty;
    private JournalMode _mode = JournalMode.Closed;
    private int _listIndex;
    private int _viewActionIndex;
    private int _currentPageIndex = -1;
    private string _draftTitle = string.Empty;
    private string _draftContent = string.Empty;
    private float _previousTimeScale = 1f;
    private TitleEntryPurpose _titleEntryPurpose;
    private bool _confirmingDelete;
    private bool _focusEditorRequested;
    private string _lastTitleObserved = string.Empty;
    private string _lastContentObserved = string.Empty;
    private int _lastTitleCursorIndex = -1;
    private int _lastContentCursorIndex = -1;
    private int _lastEditorAnnouncementFrame = -1;
    private Rect _lastContentEditorRect;

    private const string TitleEditorControlName = "JournalTitleEditor";
    private const string ContentEditorControlName = "JournalContentEditor";

    private static bool OverlayOpen => s_overlayOpen;

    private void Awake()
    {
        s_instance = this;
        _toggleJournalKey = Config.Bind("Hotkeys", "ToggleJournalV2", new KeyboardShortcut(KeyCode.F7), "Opens or closes the journal.");
        _repeatJournalKey = Config.Bind("Hotkeys", "RepeatJournalState", new KeyboardShortcut(KeyCode.F10), "Repeats the current journal page or selection while the journal is open.");
        _pasteSceneTextKey = Config.Bind("Hotkeys", "PasteCurrentSceneTextV2", new KeyboardShortcut(KeyCode.F9), "Pastes the current scene text into the current journal page or draft.");
        _savePath = Path.Combine(Paths.ConfigPath, "com.wttdp.journal.pages.json");

        InitializeStyles();
        LoadPages();

        _harmony = new Harmony(PluginInfo.Guid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        if (_toggleJournalKey.Value.IsDown())
        {
            if (OverlayOpen)
            {
                CloseJournal("Journal closed.");
            }
            else
            {
                OpenJournal();
            }
        }

        if (!OverlayOpen)
        {
            return;
        }

        if (_repeatJournalKey.Value.IsDown())
        {
            RepeatCurrentJournalState();
        }

        switch (_mode)
        {
            case JournalMode.List:
                UpdateListMode();
                break;
            case JournalMode.View:
                UpdateViewMode();
                break;
            case JournalMode.CreateTitle:
                UpdateCreateTitleMode();
                break;
            case JournalMode.EditContent:
                UpdateEditContentMode();
                break;
        }
    }

    private void OnGUI()
    {
        if (!OverlayOpen)
        {
            return;
        }

        var width = Mathf.Min(Screen.width - 80f, 900f);
        var height = Mathf.Min(Screen.height - 80f, 700f);
        var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

        GUI.Box(rect, "Journal", _windowStyle);

        GUILayout.BeginArea(new Rect(rect.x + 24f, rect.y + 56f, rect.width - 48f, rect.height - 80f));
        HandleEditorHotkeysFromGui();
        switch (_mode)
        {
            case JournalMode.List:
                DrawListMode();
                break;
            case JournalMode.View:
                DrawViewMode();
                break;
            case JournalMode.CreateTitle:
                DrawCreateTitleMode();
                break;
            case JournalMode.EditContent:
                DrawEditContentMode();
                break;
        }

        GUILayout.EndArea();
    }

    private void HandleEditorHotkeysFromGui()
    {
        var currentEvent = Event.current;
        if (currentEvent == null || currentEvent.type != EventType.KeyDown)
        {
            return;
        }

        if (_mode == JournalMode.CreateTitle)
        {
            if (currentEvent.keyCode == KeyCode.Escape)
            {
                _draftTitle = string.Empty;
                _mode = JournalMode.List;
                currentEvent.Use();
                Speak("Page creation cancelled.", interrupt: true);
                SpeakCurrentListSelection(interrupt: false);
                return;
            }

            if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
            {
                currentEvent.Use();
                FinalizeTitleEntry();
            }

            return;
        }

        if (_mode != JournalMode.EditContent)
        {
            return;
        }

        if (currentEvent.keyCode == KeyCode.Escape)
        {
            currentEvent.Use();
            SaveCurrentDraftAndReturnToView();
            return;
        }

        if (MatchesShortcut(currentEvent, _pasteSceneTextKey.Value))
        {
            currentEvent.Use();
            PasteCurrentSceneIntoDraft();
            _focusEditorRequested = true;
        }
    }

    private void OpenJournal()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Input.ResetInputAxes();
        s_overlayOpen = true;
        _mode = JournalMode.List;
        _lastTitleCursorIndex = -1;
        _lastContentCursorIndex = -1;
        _lastTitleObserved = _draftTitle;
        _lastContentObserved = _draftContent;
        _listIndex = Mathf.Clamp(_listIndex, 0, Math.Max(GetListItemCount() - 1, 0));
        Speak(BuildJournalOpenedSpeech(), interrupt: true);
        SpeakCurrentListSelection(interrupt: false);
    }

    private void CloseJournal(string speech)
    {
        SavePages();
        Time.timeScale = _previousTimeScale;
        s_overlayOpen = false;
        _mode = JournalMode.Closed;
        Input.ResetInputAxes();
        Speak(speech, interrupt: true);
    }

    private void UpdateListMode()
    {
        if (WasMoveUpPressed())
        {
            MoveListSelection(-1);
            return;
        }

        if (WasMoveDownPressed())
        {
            MoveListSelection(1);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ActivateListSelection();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseJournal("Journal closed.");
        }
    }

    private void UpdateViewMode()
    {
        if (!TryGetCurrentPage(out _))
        {
            _mode = JournalMode.List;
            SpeakCurrentListSelection(interrupt: true);
            return;
        }

        if (WasMoveUpPressed() || WasMoveDownPressed())
        {
            var delta = WasMoveUpPressed() ? -1 : 1;
            _viewActionIndex = (_viewActionIndex + delta + 7) % 7;
            Speak(GetCurrentViewActionLabel(), interrupt: true);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (_viewActionIndex == 0)
            {
                if (TryGetCurrentPage(out var page))
                {
                    Speak(BuildPageSpeech(page), interrupt: true);
                }
            }
            else if (_viewActionIndex == 1)
            {
                BeginEditingCurrentPage();
            }
            else if (_viewActionIndex == 2)
            {
                BeginRenamingCurrentPage();
            }
            else if (_viewActionIndex == 3)
            {
                ToggleDeleteConfirmation();
            }
            else if (_viewActionIndex == 4)
            {
                PasteClipboardIntoCurrentPage();
            }
            else if (_viewActionIndex == 5)
            {
                PasteCurrentSceneIntoCurrentPage();
            }
            else
            {
                _confirmingDelete = false;
                _mode = JournalMode.List;
                SpeakCurrentListSelection(interrupt: true);
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _confirmingDelete = false;
            _mode = JournalMode.List;
            SpeakCurrentListSelection(interrupt: true);
        }
    }

    private void UpdateCreateTitleMode()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _draftTitle = string.Empty;
            _mode = JournalMode.List;
            Speak("Page creation cancelled.", interrupt: true);
            SpeakCurrentListSelection(interrupt: false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            FinalizeTitleEntry();
            return;
        }
    }

    private void UpdateEditContentMode()
    {
        if (!TryGetCurrentPage(out var page))
        {
            _mode = JournalMode.List;
            SpeakCurrentListSelection(interrupt: true);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SaveCurrentDraftAndReturnToView();
            return;
        }

        if (_pasteSceneTextKey.Value.IsDown())
        {
            PasteCurrentSceneIntoDraft();
            return;
        }

        if (IsClipboardPasteShortcutPressed())
        {
            PasteClipboardIntoDraft();
        }
    }

    private void FinalizeTitleEntry()
    {
        var title = Normalize(_draftTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            Speak("Please enter a page title first.", interrupt: true);
            return;
        }

        if (_titleEntryPurpose == TitleEntryPurpose.Rename)
        {
            if (!TryGetCurrentPage(out var existingPage))
            {
                _mode = JournalMode.List;
                SpeakCurrentListSelection(interrupt: true);
                return;
            }

            existingPage.Title = title;
            existingPage.UpdatedAtUtc = DateTime.UtcNow;
            _draftTitle = title;
            SavePages();
            _mode = JournalMode.View;
            _viewActionIndex = 0;
            _confirmingDelete = false;
            Speak($"Renamed page to {title}. {BuildPageSpeech(existingPage)}", interrupt: true);
            return;
        }

        _pages.Add(new JournalPage
        {
            Title = title,
            Content = string.Empty,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _currentPageIndex = _pages.Count - 1;
        _draftTitle = title;
        _draftContent = string.Empty;
        _mode = JournalMode.EditContent;
        _focusEditorRequested = true;
        SavePages();
        Speak($"Created page {title}. Type your journal entry. Press Escape when you are done.", interrupt: true);
    }

    private void SaveCurrentDraftAndReturnToView()
    {
        if (!TryGetCurrentPage(out var page))
        {
            _mode = JournalMode.List;
            SpeakCurrentListSelection(interrupt: true);
            return;
        }

        page.Content = _draftContent;
        page.UpdatedAtUtc = DateTime.UtcNow;
        SavePages();
        _mode = JournalMode.View;
        _viewActionIndex = 0;
        Speak($"Saved page {page.Title}. {BuildPageSpeech(page)}", interrupt: true);
    }

    private void MoveListSelection(int delta)
    {
        var itemCount = GetListItemCount();
        if (itemCount <= 0)
        {
            _listIndex = 0;
            Speak("Create new page.", interrupt: true);
            return;
        }

        _listIndex = (_listIndex + delta + itemCount) % itemCount;
        SpeakCurrentListSelection(interrupt: true);
    }

    private void ActivateListSelection()
    {
        if (_listIndex < _pages.Count)
        {
            _currentPageIndex = _listIndex;
            _mode = JournalMode.View;
            _viewActionIndex = 0;
            _confirmingDelete = false;
            if (TryGetCurrentPage(out var page))
            {
                Speak(BuildPageSpeech(page) + " Read page selected.", interrupt: true);
            }

            return;
        }

        if (_listIndex == _pages.Count)
        {
            _draftTitle = string.Empty;
            _titleEntryPurpose = TitleEntryPurpose.Create;
            _mode = JournalMode.CreateTitle;
            Speak("Create new page. Type a title, then press Enter to continue. Press Escape to cancel.", interrupt: true);
            return;
        }

        CloseJournal("Journal closed.");
    }

    private void BeginEditingCurrentPage()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        _draftTitle = page.Title;
        _draftContent = page.Content ?? string.Empty;
        _mode = JournalMode.EditContent;
        _focusEditorRequested = true;
        Speak($"Editing {page.Title}. Type your entry. Press Escape when you are done.", interrupt: true);
    }

    private void BeginRenamingCurrentPage()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        _draftTitle = page.Title;
        _titleEntryPurpose = TitleEntryPurpose.Rename;
        _mode = JournalMode.CreateTitle;
        _focusEditorRequested = true;
        Speak($"Rename page {page.Title}. Type a new title, then press Enter to save. Press Escape to cancel.", interrupt: true);
    }

    private void ToggleDeleteConfirmation()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        if (!_confirmingDelete)
        {
            _confirmingDelete = true;
            Speak($"Delete page {page.Title}. Press Enter again to confirm, or move away to cancel.", interrupt: true);
            return;
        }

        DeleteCurrentPage(page);
    }

    private void DeleteCurrentPage(JournalPage page)
    {
        var deletedTitle = page.Title;
        if (_currentPageIndex >= 0 && _currentPageIndex < _pages.Count)
        {
            _pages.RemoveAt(_currentPageIndex);
        }

        _currentPageIndex = -1;
        _confirmingDelete = false;
        _mode = JournalMode.List;
        _listIndex = Mathf.Clamp(_listIndex, 0, Math.Max(GetListItemCount() - 1, 0));
        SavePages();
        Speak($"Deleted page {deletedTitle}.", interrupt: true);
        SpeakCurrentListSelection(interrupt: false);
    }

    private void PasteClipboardIntoCurrentPage()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        var clipboardText = Normalize(GetClipboardText());
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            Speak("Clipboard is empty.", interrupt: true);
            return;
        }

        page.Content = AppendTextBlock(page.Content, clipboardText);
        page.UpdatedAtUtc = DateTime.UtcNow;
        SavePages();
        _confirmingDelete = false;
        Speak($"Pasted clipboard into {page.Title}.", interrupt: true);
    }

    private void PasteCurrentSceneIntoCurrentPage()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        var sceneText = Normalize(GetCurrentSceneText());
        if (string.IsNullOrWhiteSpace(sceneText))
        {
            Speak("No current scene text is available.", interrupt: true);
            return;
        }

        page.Content = AppendTextBlock(page.Content, sceneText);
        page.UpdatedAtUtc = DateTime.UtcNow;
        SavePages();
        _confirmingDelete = false;
        Speak($"Pasted current scene text into {page.Title}.", interrupt: true);
    }

    private void PasteClipboardIntoDraft()
    {
        var clipboardText = Normalize(GetClipboardText());
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            Speak("Clipboard is empty.", interrupt: true);
            return;
        }

        _draftContent = AppendTextBlock(_draftContent, clipboardText);
        Speak("Pasted clipboard.", interrupt: true);
    }

    private void PasteCurrentSceneIntoDraft()
    {
        var sceneText = Normalize(GetCurrentSceneText());
        if (string.IsNullOrWhiteSpace(sceneText))
        {
            Speak("No current scene text is available.", interrupt: true);
            return;
        }

        _draftContent = AppendTextBlock(_draftContent, sceneText);
        Speak("Pasted current scene text.", interrupt: true);
    }

    private int GetListItemCount()
    {
        return _pages.Count + 2;
    }

    private void DrawListMode()
    {
        GUILayout.Label("Up and down move between pages. Enter opens a page or creates one. Escape closes the journal.", _bodyStyle);
        GUILayout.Space(16f);

        for (var i = 0; i < _pages.Count; i++)
        {
            GUILayout.Label($"{i + 1}. {_pages[i].Title}", i == _listIndex ? _selectedStyle : _normalStyle);
        }

        GUILayout.Space(12f);
        GUILayout.Label("Create new page", _listIndex == _pages.Count ? _selectedStyle : _normalStyle);
        GUILayout.Label("Close journal", _listIndex == _pages.Count + 1 ? _selectedStyle : _normalStyle);
    }

    private void DrawViewMode()
    {
        if (!TryGetCurrentPage(out var page))
        {
            return;
        }

        GUILayout.Label(page.Title, _selectedStyle);
        GUILayout.Space(12f);
        GUILayout.Label(string.IsNullOrWhiteSpace(page.Content) ? "(Empty page)" : page.Content, _bodyStyle);
        GUILayout.Space(24f);
        GUILayout.Label("Read page", _viewActionIndex == 0 ? _selectedStyle : _normalStyle);
        GUILayout.Label("Edit", _viewActionIndex == 1 ? _selectedStyle : _normalStyle);
        GUILayout.Label("Rename page", _viewActionIndex == 2 ? _selectedStyle : _normalStyle);
        var deleteLabel = _confirmingDelete && _viewActionIndex == 3
            ? "Delete page. Press Enter again to confirm"
            : "Delete page";
        GUILayout.Label(deleteLabel, _viewActionIndex == 3 ? _selectedStyle : _normalStyle);
        GUILayout.Label("Paste clipboard into page", _viewActionIndex == 4 ? _selectedStyle : _normalStyle);
        GUILayout.Label("Paste current scene text", _viewActionIndex == 5 ? _selectedStyle : _normalStyle);
        GUILayout.Label("Back", _viewActionIndex == 6 ? _selectedStyle : _normalStyle);
    }

    private void DrawCreateTitleMode()
    {
        var heading = _titleEntryPurpose == TitleEntryPurpose.Rename ? "Rename Page" : "Create New Page";
        GUILayout.Label(heading, _selectedStyle);
        GUILayout.Space(12f);
        GUILayout.Label("Type a title. Press Enter to continue. Escape cancels.", _bodyStyle);
        GUILayout.Space(16f);
        GUI.SetNextControlName(TitleEditorControlName);
        _draftTitle = GUILayout.TextField(_draftTitle ?? string.Empty, _bodyStyle, GUILayout.ExpandHeight(false));
        FocusEditorControlIfNeeded(TitleEditorControlName);
        AnnounceEditorState(TitleEditorControlName, ref _lastTitleObserved, ref _lastTitleCursorIndex, _draftTitle);
    }

    private void DrawEditContentMode()
    {
        var title = TryGetCurrentPage(out var page) ? page.Title : _draftTitle;
        GUILayout.Label(title, _selectedStyle);
        GUILayout.Space(12f);
        GUILayout.Label("Type your page. Use arrows to move through the text. Control V pastes clipboard text. F9 pastes the current scene text. Escape saves and goes back.", _bodyStyle);
        GUILayout.Space(16f);
        GUI.SetNextControlName(ContentEditorControlName);
        _draftContent = GUILayout.TextArea(_draftContent ?? string.Empty, _bodyStyle, GUILayout.ExpandHeight(true));
        _lastContentEditorRect = GUILayoutUtility.GetLastRect();
        FocusEditorControlIfNeeded(ContentEditorControlName);
        AnnounceEditorState(ContentEditorControlName, ref _lastContentObserved, ref _lastContentCursorIndex, _draftContent);
    }

    private string BuildJournalOpenedSpeech()
    {
        return _pages.Count == 0
            ? "Journal opened. No pages yet."
            : $"Journal opened. {_pages.Count} page{(_pages.Count == 1 ? string.Empty : "s")}.";
    }

    private void SpeakCurrentListSelection(bool interrupt)
    {
        if (_listIndex < _pages.Count)
        {
            Speak($"Page {_listIndex + 1}. {_pages[_listIndex].Title}.", interrupt);
            return;
        }

        if (_listIndex == _pages.Count)
        {
            Speak("Create new page.", interrupt);
            return;
        }

        Speak("Close journal.", interrupt);
    }

    private string GetCurrentViewActionLabel()
    {
        return _viewActionIndex switch
        {
            0 => "Read page",
            1 => "Edit",
            2 => "Rename page",
            3 => _confirmingDelete ? "Delete page. Press Enter again to confirm" : "Delete page",
            4 => "Paste clipboard into page",
            5 => "Paste current scene text",
            _ => "Back"
        };
    }

    private void RepeatCurrentJournalState()
    {
        switch (_mode)
        {
            case JournalMode.List:
                SpeakCurrentListSelection(interrupt: true);
                break;
            case JournalMode.View:
                if (TryGetCurrentPage(out var page))
                {
                    Speak(BuildPageSpeech(page) + " " + GetCurrentViewActionLabel(), interrupt: true);
                }

                break;
            case JournalMode.CreateTitle:
                Speak(string.IsNullOrWhiteSpace(_draftTitle)
                    ? "Type a page title."
                    : $"Title. {Normalize(_draftTitle)}", interrupt: true);
                break;
            case JournalMode.EditContent:
                if (TryGetCurrentPage(out var editingPage))
                {
                    var content = Normalize(_draftContent);
                    Speak(string.IsNullOrWhiteSpace(content)
                        ? $"Editing {editingPage.Title}. Empty page."
                        : $"Editing {editingPage.Title}. {content}", interrupt: true);
                }

                break;
        }
    }

    private static string BuildPageSpeech(JournalPage page)
    {
        var content = Normalize(page.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"Page {page.Title}. Empty page.";
        }

        return $"Page {page.Title}. {content}";
    }

    private static bool IsClipboardPasteShortcutPressed()
    {
        var controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        return controlHeld && Input.GetKeyDown(KeyCode.V);
    }

    private static bool MatchesShortcut(Event currentEvent, KeyboardShortcut shortcut)
    {
        if (currentEvent.keyCode != shortcut.MainKey)
        {
            return false;
        }

        var requiresShift = shortcut.Modifiers.Contains(KeyCode.LeftShift) || shortcut.Modifiers.Contains(KeyCode.RightShift);
        var requiresControl = shortcut.Modifiers.Contains(KeyCode.LeftControl) || shortcut.Modifiers.Contains(KeyCode.RightControl);
        var requiresAlt = shortcut.Modifiers.Contains(KeyCode.LeftAlt) || shortcut.Modifiers.Contains(KeyCode.RightAlt);
        var hasModifiers = shortcut.Modifiers.Any();

        if (!hasModifiers)
        {
            return !currentEvent.shift && !currentEvent.control && !currentEvent.alt;
        }

        return currentEvent.shift == requiresShift
            && currentEvent.control == requiresControl
            && currentEvent.alt == requiresAlt;
    }

    private void AnnounceEditorState(string controlName, ref string lastObservedText, ref int lastCursorIndex, string currentText)
    {
        if (Event.current == null || GUI.GetNameOfFocusedControl() != controlName)
        {
            lastObservedText = currentText ?? string.Empty;
            return;
        }

        if (Time.frameCount == _lastEditorAnnouncementFrame)
        {
            return;
        }

        if (Event.current.type != EventType.Repaint && Event.current.type != EventType.KeyDown)
        {
            return;
        }

        var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
        if (editor == null)
        {
            lastObservedText = currentText ?? string.Empty;
            return;
        }

        var normalizedCurrent = currentText ?? string.Empty;
        var previousText = lastObservedText ?? string.Empty;
        var currentCursorIndex = Mathf.Clamp(editor.cursorIndex, 0, normalizedCurrent.Length);
        string announcement;
        if (previousText == normalizedCurrent
            && lastCursorIndex != currentCursorIndex
            && currentCursorIndex >= 0
            && controlName == ContentEditorControlName
            && Event.current != null
            && (Event.current.keyCode == KeyCode.UpArrow || Event.current.keyCode == KeyCode.DownArrow)
            && _lastContentEditorRect.width > 1f)
        {
            announcement = DescribeCurrentVisualLine(normalizedCurrent, currentCursorIndex, _lastContentEditorRect);
        }
        else
        {
            announcement = GetEditorAnnouncement(previousText, normalizedCurrent, lastCursorIndex, currentCursorIndex, Event.current);
        }

        lastObservedText = normalizedCurrent;
        lastCursorIndex = currentCursorIndex;

        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        _lastEditorAnnouncementFrame = Time.frameCount;
        Speak(announcement, interrupt: true);
    }

    private static string GetEditorAnnouncement(string previousText, string currentText, int previousCursorIndex, int currentCursorIndex, Event? currentEvent)
    {
        if (previousText != currentText)
        {
            var deletedCharacter = TryGetDeletedCharacter(previousText, currentText);
            if (deletedCharacter.HasValue)
            {
                return $"Deleted {DescribeCharacter(deletedCharacter.Value)}";
            }

            var insertedText = TryGetInsertedText(previousText, currentText);
            if (!string.IsNullOrWhiteSpace(insertedText))
            {
                return DescribeInsertedText(insertedText);
            }

            return "Text changed.";
        }

        if (previousCursorIndex != currentCursorIndex && currentCursorIndex >= 0)
        {
            if (currentEvent != null && (currentEvent.keyCode == KeyCode.UpArrow || currentEvent.keyCode == KeyCode.DownArrow))
            {
                return DescribeCurrentLine(currentText, currentCursorIndex);
            }

            return DescribeCursorRightCharacter(currentText, currentCursorIndex);
        }

        return string.Empty;
    }

    private static char? TryGetDeletedCharacter(string previousText, string currentText)
    {
        if (previousText.Length != currentText.Length + 1)
        {
            return null;
        }

        var index = 0;
        while (index < currentText.Length && previousText[index] == currentText[index])
        {
            index++;
        }

        return previousText[index];
    }

    private static string TryGetInsertedText(string previousText, string currentText)
    {
        if (currentText.Length <= previousText.Length)
        {
            return string.Empty;
        }

        var prefix = 0;
        while (prefix < previousText.Length && previousText[prefix] == currentText[prefix])
        {
            prefix++;
        }

        var previousSuffix = previousText.Length - 1;
        var currentSuffix = currentText.Length - 1;
        while (previousSuffix >= prefix && currentSuffix >= prefix && previousText[previousSuffix] == currentText[currentSuffix])
        {
            previousSuffix--;
            currentSuffix--;
        }

        return currentText.Substring(prefix, currentSuffix - prefix + 1);
    }

    private static string DescribeInsertedText(string insertedText)
    {
        if (insertedText.Length == 1)
        {
            return DescribeCharacter(insertedText[0]);
        }

        return insertedText.Replace("\r", string.Empty).Replace("\n", " new line ");
    }

    private static string DescribeCursorRightCharacter(string text, int cursorIndex)
    {
        if (cursorIndex >= text.Length)
        {
            return "End";
        }

        return DescribeCharacter(text[cursorIndex]);
    }

    private static string DescribeCurrentLine(string text, int cursorIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "Blank line";
        }

        var safeIndex = Mathf.Clamp(cursorIndex, 0, text.Length);
        var lineStart = safeIndex;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var lineEnd = safeIndex;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
        {
            lineEnd++;
        }

        var line = text.Substring(lineStart, lineEnd - lineStart).Trim();
        return string.IsNullOrWhiteSpace(line) ? "Blank line" : line;
    }

    private string DescribeCurrentVisualLine(string text, int cursorIndex, Rect textAreaRect)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "Blank line";
        }

        var safeIndex = Mathf.Clamp(cursorIndex, 0, text.Length);
        var content = new GUIContent(text);
        var referenceY = GetCursorLineY(textAreaRect, content, safeIndex);
        var epsilon = 0.5f;

        var lineStart = safeIndex;
        while (lineStart > 0)
        {
            var previousIndex = lineStart - 1;
            if (text[previousIndex] == '\n')
            {
                break;
            }

            var previousY = GetCursorLineY(textAreaRect, content, previousIndex);
            if (Mathf.Abs(previousY - referenceY) > epsilon)
            {
                break;
            }

            lineStart = previousIndex;
        }

        var lineEnd = safeIndex;
        while (lineEnd < text.Length)
        {
            if (text[lineEnd] == '\n')
            {
                break;
            }

            var nextIndex = lineEnd + 1;
            if (nextIndex > text.Length)
            {
                break;
            }

            var nextY = GetCursorLineY(textAreaRect, content, nextIndex);
            if (Mathf.Abs(nextY - referenceY) > epsilon)
            {
                break;
            }

            lineEnd = nextIndex;
        }

        var visualLine = text.Substring(lineStart, Math.Max(0, lineEnd - lineStart)).Trim();
        return string.IsNullOrWhiteSpace(visualLine) ? "Blank line" : visualLine;
    }

    private float GetCursorLineY(Rect textAreaRect, GUIContent content, int stringIndex)
    {
        var cursorPosition = _bodyStyle.GetCursorPixelPosition(textAreaRect, content, stringIndex);
        return cursorPosition.y;
    }

    private static string DescribeCharacter(char value)
    {
        return value switch
        {
            ' ' => "space",
            '\n' => "new line",
            '\r' => "new line",
            '\t' => "tab",
            _ => value.ToString()
        };
    }

    private string GetClipboardText()
    {
        try
        {
            return GUIUtility.systemCopyBuffer ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetCurrentSceneText()
    {
        try
        {
            var roomManager = Resources.FindObjectsOfTypeAll<RoomManager>().FirstOrDefault();
            if (roomManager == null)
            {
                return string.Empty;
            }

            var currentNodeField = typeof(RoomManager).GetField("currentNode", _fieldFlags);
            var currentNode = currentNodeField?.GetValue(roomManager);
            if (currentNode == null)
            {
                return string.Empty;
            }

            var nodeType = currentNode.GetType();
            var mainText = Normalize(nodeType.GetField("m_text", _fieldFlags)?.GetValue(currentNode) as string);
            var itemText = Normalize(nodeType.GetField("ItemString", _fieldFlags)?.GetValue(currentNode) as string);
            var removedItemText = Normalize(nodeType.GetField("RemoveItem", _fieldFlags)?.GetValue(currentNode) as string);

            var parts = new[] { mainText, itemText, removedItemText }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string AppendTextBlock(string existingText, string newText)
    {
        var existing = existingText ?? string.Empty;
        var incoming = newText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming;
        }

        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        return existing.TrimEnd() + Environment.NewLine + Environment.NewLine + incoming.TrimStart();
    }

    private void FocusEditorControlIfNeeded(string controlName)
    {
        if (!_focusEditorRequested)
        {
            return;
        }

        GUI.FocusControl(controlName);
        _focusEditorRequested = false;
    }

    private bool TryGetCurrentPage(out JournalPage page)
    {
        if (_currentPageIndex >= 0 && _currentPageIndex < _pages.Count)
        {
            page = _pages[_currentPageIndex];
            return true;
        }

        page = null!;
        return false;
    }

    private void LoadPages()
    {
        if (!File.Exists(_savePath))
        {
            return;
        }

        try
        {
            using (var stream = File.OpenRead(_savePath))
            {
                var serializer = new DataContractJsonSerializer(typeof(JournalSaveData));
                if (serializer.ReadObject(stream) is JournalSaveData data && data.Pages != null)
                {
                    _pages.Clear();
                    _pages.AddRange(data.Pages.Where(page => page != null));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to load journal pages: {ex.Message}");
        }
    }

    private void SavePages()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_savePath) ?? Paths.ConfigPath);
            using (var stream = File.Create(_savePath))
            {
                var serializer = new DataContractJsonSerializer(typeof(JournalSaveData));
                serializer.WriteObject(stream, new JournalSaveData { Pages = _pages });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to save journal pages: {ex.Message}");
        }
    }

    private void InitializeStyles()
    {
        _windowStyle.fontSize = 28;
        _windowStyle.alignment = TextAnchor.UpperCenter;

        _bodyStyle.wordWrap = true;
        _bodyStyle.fontSize = 22;
        _bodyStyle.normal.textColor = Color.white;

        _selectedStyle.wordWrap = true;
        _selectedStyle.fontSize = 24;
        _selectedStyle.normal.textColor = Color.yellow;

        _normalStyle.wordWrap = true;
        _normalStyle.fontSize = 22;
        _normalStyle.normal.textColor = Color.white;
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

    private static bool WasMoveUpPressed()
    {
        return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
    }

    private static bool WasMoveDownPressed()
    {
        return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text!.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    [HarmonyPatch(typeof(RoomManager), "Update")]
    private static class RoomManagerUpdatePatch
    {
        private static bool Prefix()
        {
            return !OverlayOpen;
        }
    }

    [HarmonyPatch(typeof(StandaloneInputModule), "Process")]
    private static class StandaloneInputModuleProcessPatch
    {
        private static bool Prefix()
        {
            return !OverlayOpen;
        }
    }

    private enum JournalMode
    {
        Closed,
        List,
        View,
        CreateTitle,
        EditContent
    }

    private enum TitleEntryPurpose
    {
        Create,
        Rename
    }

    [DataContract]
    private sealed class JournalSaveData
    {
        [DataMember]
        public List<JournalPage> Pages { get; set; } = new List<JournalPage>();
    }

    [DataContract]
    private sealed class JournalPage
    {
        [DataMember]
        public string Title { get; set; } = string.Empty;

        [DataMember]
        public string Content { get; set; } = string.Empty;

        [DataMember]
        public DateTime UpdatedAtUtc { get; set; }
    }
}
