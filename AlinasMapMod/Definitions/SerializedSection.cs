using System.Collections.Generic;
using System.Linq;
using AlinasMapMod.Validation;
using AlinasMapMod.Caches;
using Game.Progression;
using HarmonyLib;
using Model.Ops;
using Serilog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlinasMapMod.Definitions;

public class SerializedSection : SerializedComponentBase<Section>,
  ICreatableComponent<Section>,
  IDestroyableComponent<Section>
{
  private static readonly AccessTools.FieldRef<InterchangeTransfer, Interchange> interchangeTransferFrom =
    AccessTools.FieldRefAccess<InterchangeTransfer, Interchange>("from");
  private static readonly AccessTools.FieldRef<InterchangeTransfer, Interchange> interchangeTransferTo =
    AccessTools.FieldRefAccess<InterchangeTransfer, Interchange>("to");
  
  public string DisplayName { get; set; } = "";
  public string Description { get; set; } = "";
  public Dictionary<string, bool> PrerequisiteSections { get; set; } = new Dictionary<string, bool>();
  public IEnumerable<SerializedDeliveryPhase> DeliveryPhases { get; set; } = new List<SerializedDeliveryPhase>();
  public Dictionary<string, bool> DisableFeaturesOnUnlock { get; set; } = new Dictionary<string, bool>();
  public Dictionary<string, bool> EnableFeaturesOnUnlock { get; set; } = new Dictionary<string, bool>();
  public Dictionary<string, bool> EnableFeaturesOnAvailable { get; set; } = new Dictionary<string, bool>();

  public Dictionary<string, string> InterchangeTransfers { get; set; } = new();
  
  public SerializedSection()
  {
  }

  public SerializedSection(Section s)
  {
    Read(s);
  }

  protected override void ConfigureValidation()
  {
    RuleFor(() => DisplayName)
      .Required();
    
    RuleFor(() => Description)
      .Required();

    // Validate delivery phases - will be validated when Write is called
  }

  public override Section Create(string id)
  {
    // Create GameObject with Section component (based on progression patterns)
    var go = new GameObject(id);
    var section = go.AddComponent<Section>();
    section.identifier = id;
    
    // Register in cache
    SectionCache.Instance[SectionCache.GetSectionIdentifier(section)] = section;
    
    // Apply configuration
    Write(section);
    
    return section;
  }

  public override void Write(Section section)
  {
    foreach (KeyValuePair<string, bool> keyValuePair in DisableFeaturesOnUnlock)
      Log.Information($"DisableFeatureOnUnlock {keyValuePair.Key} is {keyValuePair.Value}");
    
    section.displayName = DisplayName;
    section.description = Description;
    section.prerequisiteSections = DefinitionUtils.ApplyList(section, section.prerequisiteSections ?? [], PrerequisiteSections);
    section.disableFeaturesOnUnlock = DefinitionUtils.ApplyList(section.disableFeaturesOnUnlock ?? [], DisableFeaturesOnUnlock);
    section.enableFeaturesOnUnlock = DefinitionUtils.ApplyList(section.enableFeaturesOnUnlock ?? [], EnableFeaturesOnUnlock);
    section.enableFeaturesOnAvailable = DefinitionUtils.ApplyList(section.enableFeaturesOnAvailable ?? [], EnableFeaturesOnAvailable);

    section.deliveryPhases = DeliveryPhases.Select(dp => {
      var phase = new Section.DeliveryPhase();
      dp.Write(phase); // Use new Write method
      return phase;
    }).ToArray();
    
    foreach (var pair in InterchangeTransfers) {
      var existing = section.GetComponentsInChildren<InterchangeTransfer>()
        .Where(i => interchangeTransferFrom(i).Identifier == pair.Key).FirstOrDefault();
      if (existing != null) {
        Object.DestroyImmediate(existing.gameObject);
      }
      if (pair.Value == null) continue;

      if (!IndustryComponentCache.Instance.TryGetValue(pair.Key, out var first) ||
          !IndustryComponentCache.Instance.TryGetValue(pair.Value, out var second)) {
        Log.Warning($"Unable to find both industry components to transfer interchanges from {pair.Key} to {pair.Value}.");
        continue;
      }
      
      Log.Information($"Found {section.GetComponentInParent<Progression>(true)?.identifier ?? "null progression!"}");
      Log.Information($"Adding interchange transfer to {section.identifier} from {pair.Key} to {pair.Value}.");
      GameObject go = new GameObject($"intch-transfer-{section.identifier}-{pair.Key}");
      go.transform.SetParent(section.transform, false);
      var transfer = go.AddComponent<InterchangeTransfer>();
      interchangeTransferFrom(transfer) = first as Interchange;
      interchangeTransferTo(transfer) = second as Interchange;
    }
    AccessTools.Method(typeof(Section), "Awake").Invoke(section, null);
    foreach (InterchangeTransfer ch in section.GetComponentsInChildren<InterchangeTransfer>(true))
      Log.Information($"Section interchange {section.identifier}: {interchangeTransferFrom(ch).Identifier} -> {interchangeTransferTo(ch).Identifier}");
  }

  public override void Read(Section section)
  {
    DisplayName = section.displayName;
    Description = section.description;
    PrerequisiteSections = section.prerequisiteSections.ToDictionary(s => s.identifier, s => true);
    DeliveryPhases = section.deliveryPhases.Select(dp => new SerializedDeliveryPhase(dp));
    DisableFeaturesOnUnlock = section.disableFeaturesOnUnlock.ToDictionary(f => f.identifier, f => true);
    EnableFeaturesOnUnlock = section.enableFeaturesOnUnlock.ToDictionary(f => f.identifier, f => true);
    EnableFeaturesOnAvailable = section.enableFeaturesOnAvailable.ToDictionary(f => f.identifier, f => true);
    InterchangeTransfers = new Dictionary<string, string>(section.GetComponentsInChildren<InterchangeTransfer>(true).ToDictionary(t => {
      Log.Information($"Processing interchange transfer for {section.identifier}: {interchangeTransferFrom(t).Identifier} -> {interchangeTransferTo(t).Identifier}");
      return interchangeTransferFrom(t).Identifier;
    }, t => interchangeTransferTo(t).Identifier));
  }

  public static void DestroySection(Section section)
  {
    // Remove from cache
    SectionCache.Instance.Remove(SectionCache.GetSectionIdentifier(section));
    
    // Destroy GameObject
    GameObject.Destroy(section.gameObject);
  }

  public void Destroy(Section section)
  {
    DestroySection(section);
  }

  internal void ApplyTo(Section section)
  {
    // Validate before applying
    Validate();
    Write(section);
  }
}
