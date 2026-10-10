using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using DofusSharp.Dofocus.ApiClients.Models.Items;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private string PresetMessageCssClass()
    {
        return _presetMessage?.StartsWith(
            "⚠",
            StringComparison.Ordinal
        ) == true
            ? "text-danger"
            : "text-success";
    }

    private SearchFilterPreset? GetSelectedPreset()
    {
        if (_selectedPresetId is not Guid id)
        {
            return null;
        }

        return _filterPresets.FirstOrDefault(
            preset => preset.Id == id
        );
    }

    private string PresetDisplayName(
        SearchFilterPreset preset)
    {
        bool modified =
            _selectedPresetId == preset.Id &&
            IsSelectedPresetModified();

        return modified
            ? $"{preset.Name} • modifié"
            : preset.Name;
    }

    private bool IsSelectedPresetModified()
    {
        SearchFilterPreset? preset =
            GetSelectedPreset();

        if (preset is null ||
            Model is null)
        {
            return false;
        }

        return preset.SortOrder != _sortOrder ||
            !string.Equals(
                JsonSerializer.Serialize(
                    preset.Filters
                ),
                JsonSerializer.Serialize(
                    Model
                ),
                StringComparison.Ordinal
            );
    }

    private static SearchItemsModel CloneSearchModel(
        SearchItemsModel model)
    {
        string json =
            JsonSerializer.Serialize(
                model
            );

        return JsonSerializer
            .Deserialize<SearchItemsModel>(
                json
            )
            ?? new SearchItemsModel();
    }

    private void PersistFilterPresets()
    {
        _filterPresets =
            _filterPresets
                .OrderBy(
                    preset => preset.Name,
                    StringComparer.CurrentCultureIgnoreCase
                )
                .ToList();

        BestCrushSettings
            .SaveSearchFilterPresets(
                _filterPresets
            );
    }

    private void BeginNewPreset()
    {
        Model = new SearchItemsModel();
        _sortOrder = SortOrder.BestBenefit;
        _selectedPresetId = null;
        _presetEditorMode = "create";
        _presetNameDraft = string.Empty;
        _confirmDeletePreset = false;
        _presetMessage = null;

        _allItems = null;
        _rankedItems = null;
        _unrankedItems = null;
    }

    private void SaveCurrentPreset()
    {
        SearchFilterPreset? preset =
            GetSelectedPreset();

        if (preset is null)
        {
            _presetEditorMode = "create";
            _presetNameDraft = string.Empty;
            _presetMessage = null;
            return;
        }

        preset.Filters =
            CloneSearchModel(
                Model ?? new SearchItemsModel()
            );

        preset.SortOrder =
            _sortOrder;

        PersistFilterPresets();

        _presetMessage =
            $"✓ Filtre « {preset.Name} » mis à jour.";
    }

    private void BeginRenamePreset()
    {
        SearchFilterPreset? preset =
            GetSelectedPreset();

        if (preset is null)
        {
            return;
        }

        _presetEditorMode = "rename";
        _presetNameDraft = preset.Name;
        _presetMessage = null;
    }

    private void ConfirmPresetEditor()
    {
        string name =
            _presetNameDraft.Trim();

        if (string.IsNullOrWhiteSpace(
            name))
        {
            _presetMessage =
                "⚠ Donne un nom au filtre.";
            return;
        }

        SearchFilterPreset? selected =
            GetSelectedPreset();

        bool duplicateName =
            _filterPresets.Any(
                preset =>
                    !ReferenceEquals(
                        preset,
                        selected
                    ) &&
                    string.Equals(
                        preset.Name,
                        name,
                        StringComparison.CurrentCultureIgnoreCase
                    )
            );

        if (duplicateName)
        {
            _presetMessage =
                "⚠ Un filtre porte déjà ce nom.";
            return;
        }

        if (string.Equals(
            _presetEditorMode,
            "rename",
            StringComparison.Ordinal))
        {
            if (selected is null)
            {
                CancelPresetEditor();
                return;
            }

            selected.Name = name;
            PersistFilterPresets();
            _presetMessage =
                $"✓ Filtre renommé « {name} ».";
        }
        else
        {
            SearchFilterPreset preset =
                new()
                {
                    Name = name,
                    Filters = CloneSearchModel(
                        Model ?? new SearchItemsModel()
                    ),
                    SortOrder = _sortOrder
                };

            _filterPresets.Add(
                preset
            );

            _selectedPresetId =
                preset.Id;

            PersistFilterPresets();
            _presetMessage =
                $"✓ Filtre « {name} » créé.";
        }

        _presetEditorMode = null;
        _presetNameDraft = string.Empty;
    }

    private void CancelPresetEditor()
    {
        _presetEditorMode = null;
        _presetNameDraft = string.Empty;
    }

    private void DeleteSelectedPreset()
    {
        SearchFilterPreset? preset =
            GetSelectedPreset();

        if (preset is null)
        {
            _confirmDeletePreset = false;
            return;
        }

        string name = preset.Name;

        _filterPresets.Remove(
            preset
        );

        PersistFilterPresets();

        _selectedPresetId = null;
        _confirmDeletePreset = false;
        _presetEditorMode = null;
        _presetMessage =
            $"✓ Filtre « {name} » supprimé.";
    }

    private async Task ApplyPresetAsync(
        ChangeEventArgs args)
    {
        string? rawValue =
            args.Value?.ToString();

        if (!Guid.TryParse(
            rawValue,
            out Guid id))
        {
            _selectedPresetId = null;
            return;
        }

        SearchFilterPreset? preset =
            _filterPresets.FirstOrDefault(
                candidate => candidate.Id == id
            );

        if (preset is null)
        {
            return;
        }

        _selectedPresetId = id;
        Model = CloneSearchModel(
            preset.Filters
        );
        _sortOrder = preset.SortOrder;
        _presetEditorMode = null;
        _confirmDeletePreset = false;
        _presetMessage = null;

        SaveSessionState();

        await Search(
            forceRefresh: false
        );
    }

}
