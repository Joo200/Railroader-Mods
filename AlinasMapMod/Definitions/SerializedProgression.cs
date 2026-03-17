using System.Collections.Generic;
using System.Linq;
using AlinasMapMod.Caches;
using AlinasMapMod.Validation;
using Game.Progression;
using Serilog;
using UnityEngine;

namespace AlinasMapMod.Definitions;

public class SerializedProgression : SerializedComponentBase<Progression>,
  ICreatableComponent<Progression>,
  IDestroyableComponent<Progression>
{
  public string BaseProgression;
  public string FallbackSection;
  public Dictionary<string, SerializedSection> Sections { get; set; } = new Dictionary<string, SerializedSection>();

  public SerializedProgression()
  {
  }
  public SerializedProgression(Progression progression)
  {
    Read(progression);
  }

  protected override void ConfigureValidation()
  {
    // Validate that we have at least one section (warning only)
    RuleFor(() => Sections)
      .Custom((sections, context) =>
      {
        var result = new ValidationResult { IsValid = true };
        if (sections?.Count == 0)
        {
          // This is a warning, not an error - progression can exist without sections initially
          result.Warnings.Add(new ValidationWarning
          {
            Field = nameof(Sections),
            Message = "Progression has no sections defined",
            Value = sections
          });
        }
        return result;
      });

    // Validate section keys are not empty using Custom validation
    RuleFor(() => Sections)
      .Custom((sections, context) =>
      {
        var result = new ValidationResult { IsValid = true };
        
        if (sections != null)
        {
          foreach (var kvp in sections)
          {
            if (string.IsNullOrEmpty(kvp.Key))
            {
              result.IsValid = false;
              result.Errors.Add(new ValidationError
              {
                Field = $"{nameof(Sections)}[{kvp.Key}]",
                Message = "Section key cannot be null or empty",
                Code = "REQUIRED",
                Value = kvp.Key
              });
            }
          }
        }
        
        return result;
      });

    // Note: Section validation will be handled when Write is called
  }

  public override Progression Create(string id)
  {
    // Find the Progressions parent GameObject (based on OldPatcher pattern)
    var progressionsObj = GameObject.Find("Progressions");
    if (progressionsObj == null)
    {
      throw new System.InvalidOperationException("Progressions GameObject not found");
    }

    // Create GameObject and Progression component
    var go = new GameObject(id);
    go.transform.SetParent(progressionsObj.transform);
    var progression = go.AddComponent<Progression>();
    progression.identifier = id;
    
    // Set up MapFeatureManager reference (from OldPatcher pattern)
    var mapFeatureManager = Object.FindObjectOfType<MapFeatureManager>(false);
    if (mapFeatureManager != null)
    {
      progression.mapFeatureManager = mapFeatureManager;
    }
    
    go.SetActive(true);
    
    // Register in cache
    ProgressionCache.Instance[id] = progression;
    
    // Apply configuration
    Write(progression);
    
    return progression;
  }

  public override void Write(Progression progression)
  {
    // Find all existing sections in this progression instance
    foreach (var pair in Sections) {
      var identifier = pair.Key;
      var section = pair.Value;
      Log.Information("Patching section {id}", identifier);

      if (SectionCache.Instance.TryGetValue(SectionCache.GetSectionIdentifier(progression.identifier, identifier), out var sec)) {
        if (section == null) {
          SerializedSection.DestroySection(sec);
          Log.Error("Deleting section {id}", identifier);
          continue;
        }
        
        section.Write(sec);
      } else if (section != null) {
        var go = new GameObject(identifier);
        go.transform.SetParent(progression.transform);
        sec = go.AddComponent<Section>();
        sec.identifier = identifier;
        
        Log.Information("Adding section {id}", identifier);
        // Also add to global cache if not present (or update it if it's new)
        SectionCache.Instance[SectionCache.GetSectionIdentifier(sec)] = sec;
        
        section.Write(sec);
      }
    }

    if (FallbackSection != null) {
      if (SectionCache.Instance.TryGetValue(SectionCache.GetSectionIdentifier(progression.identifier, FallbackSection),
            out var sec)) {
        foreach (Section section in progression.GetComponentsInChildren<Section>())
        {
          if (Sections.ContainsKey(section.identifier))
            continue;
          if (section.prerequisiteSections.Length > 0)
            continue;
          Log.Information("Adding section {id} as fallback for legacy old section {old}", FallbackSection, section.identifier);
          section.prerequisiteSections = [sec];
        }
      }
    }
    
    // Since Section.Configure uses GetComponentsInChildren<Section>(), we reorder GameObjects in the Transform hierarchy.
    var sectionComponents = progression.GetComponentsInChildren<Section>(true);
    var sectionList = sectionComponents.ToList();
    var orderedList = new List<Section>();
    var remaining = new List<Section>(sectionList);

    // Sort based on prerequisiteSections.
    // Prerequisites must come before the section that depends on them.
    bool changed = true;
    while (remaining.Count > 0 && changed) {
      changed = false;
      for (int i = 0; i < remaining.Count; i++) {
        var s = remaining[i];
        
        // Check if all prerequisites of s are already in orderedList or NOT in this progression at all
        // (prerequisites could be in a different progression, though usually they are in the same one).
        // If a prerequisite is still in 'remaining', we cannot add 's' yet.
        bool allPrerequisitesSatisfied = true;
        if (s.prerequisiteSections != null) {
          foreach (var prereq in s.prerequisiteSections) {
            if (prereq == null) continue;
            if (remaining.Contains(prereq)) {
              allPrerequisitesSatisfied = false;
              break;
            }
          }
        }

        if (allPrerequisitesSatisfied) {
          orderedList.Add(s);
          remaining.RemoveAt(i);
          changed = true;
          break;
        }
      }
    }

    // Add any leftovers (could be cycles or missing references)
    orderedList.AddRange(remaining);

    // Apply the order to the Transform hierarchy
    for (int i = 0; i < orderedList.Count; i++) {
      orderedList[i].transform.SetSiblingIndex(i);
    }
  }

  public override void Read(Progression progression)
  {
    Log.Information("Serializing progression {id} {progression}", progression.identifier, progression.name);
    var sections = progression.GetComponentsInChildren<Section>();
    Sections = sections.ToDictionary(s => s.identifier, s => new SerializedSection(s));
  }

  public void Destroy(Progression progression)
  {
    // Remove from cache
    ProgressionCache.Instance.Remove(progression.identifier);
    
    // Destroy GameObject (this will also destroy child Section components)
    GameObject.Destroy(progression.gameObject);
  }

  public void ApplyTo(Progression progression)
  {
    // Validate before applying
    Validate();
    Write(progression);
  }
}
