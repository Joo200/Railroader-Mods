using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlinasMapMod.Caches;
using AlinasMapMod.Definitions;
using GalaSoft.MvvmLight.Messaging;
using Game.Progression;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Serilog;
using UnityEngine;

namespace AlinasMapMod;

public class OldPatcher
{
  private static readonly AccessTools.FieldRef<Progression, MapFeature[]> enableFeaturesAtStart =
    AccessTools.FieldRefAccess<Progression, MapFeature[]>("enableFeaturesAtStart");

  private PatchState _initialState;

  public PatchState InitialState
  {
    get {
      if (_initialState == null) {
        _initialState = Dump();
      }

      return _initialState;
    }
  }

  public delegate void PatchStateHandler(PatchState state);

  public event PatchStateHandler OnPatchState;

  protected virtual void OnPatchStateEvent(PatchState state)
  {
    OnPatchState?.Invoke(state);
  }

  public void Patch()
  {
    Messenger.Default.Send(new CachesNeedRebuildEvent());
    var mapFeatureManager = UnityEngine.Object.FindObjectOfType<MapFeatureManager>(false);

    JObject baseJson = new JObject();
    foreach (var mixinto in AlinasMapModPlugin.Shared.GetMixintos("progressions")) {
      var json = JObject.Parse(File.ReadAllText(mixinto.Mixinto));
      baseJson.Merge(json);
    }

    var state = baseJson.ToObject<PatchState>();
    OnPatchStateEvent(state);

    foreach (KeyValuePair<string, SerializedMapFeature> pair in state.MapFeatures) {
      try {
        var identifier = pair.Key;
        var mapFeature = pair.Value;
        if (mapFeature == null) {
          Log.Warning("MapFeatures cannot be deleted. {id}", identifier);
          continue;
        }

        if (MapFeatureCache.Instance.TryGetValue(identifier, out var existing)) {
          Log.Information("Patching MapFeature {id}", identifier);
          try {
            mapFeature.ApplyTo(existing); // This can throw exceptions if the map feature is not valid
          } catch (Exception e) {
            Log.Error(e, "Failed to apply map feature {id}", identifier);
            continue; // Skip this map feature if it fails
          }
        } else {
          Log.Warning("Creating MapFeature {id}", identifier);
          var go = new GameObject(identifier);
          go.transform.SetParent(mapFeatureManager.transform);
          existing = go.AddComponent<MapFeature>();
          existing.identifier = identifier;
          MapFeatureCache.Instance[identifier] = existing;
          try {
            // ApplyTo can throw exceptions if the map feature is not valid
            mapFeature.ApplyTo(existing);
          } catch (Exception e) {
            Log.Error(e, "Failed to apply map feature {id}", identifier);
            GameObject.Destroy(existing.gameObject);
            continue;
          }
        }

        Log.Information("Patching MapFeature {id}", identifier);
      } catch (Exception e) {
        Log.Error(e, "Failed to patch map features for {feature}", pair.Key);
      }
    }

    try {
      foreach (KeyValuePair<string, SerializedProgression> pair in state.Progressions) {
        var identifier = pair.Key;
        var progression = pair.Value;
        if (progression.BaseProgression == null)
          PatchProgression(mapFeatureManager, identifier, progression);
      }

      foreach (KeyValuePair<string, SerializedProgression> pair in state.Progressions) {
        var identifier = pair.Key;
        var progression = pair.Value;
        if (progression.BaseProgression != null)
          PatchProgression(mapFeatureManager, identifier, progression);
      }
    } catch (Exception e) {
      Log.Error(e, "Failed to patch progressions");
    }
  }

  private void PatchProgression(MapFeatureManager mapFeatureManager, string identifier,
    SerializedProgression progression)
  {
    if (progression == null) {
      Log.Warning("Progressions cannot be deleted. {id}", identifier);
      return;
    }

    if (ProgressionCache.Instance.TryGetValue(identifier, out var comp)) {
      Log.Information("Patching progression {id}", identifier);
      progression.ApplyTo(comp);
    } else {
      Log.Warning("Progression missing {id}", identifier);
      var progressionsObj = GameObject.Find("Progressions");
      GameObject go;
      Log.Information("Creating progression {id}", identifier);
      go = new GameObject(identifier);
      go.transform.SetParent(progressionsObj.transform);
      comp = go.AddComponent<Progression>();
      comp.identifier = identifier;
      comp.mapFeatureManager = mapFeatureManager;
      
      if (!string.IsNullOrEmpty(progression.BaseProgression)) {
        if (ProgressionCache.Instance.TryGetValue(progression.BaseProgression, out var baseProgression)) {
          var existing = new SerializedProgression(baseProgression);
          try {
            existing.ApplyTo(comp);  
          } catch (Exception e) {
            Log.Error(e, "Failed to apply base progression {base} to {id}", progression.BaseProgression, identifier);
            GameObject.Destroy(comp.gameObject);
            return;
          }
          
        } else {
          Log.Warning("Base progression not found: {id}", progression.BaseProgression);
        }
      }

      go.SetActive(true);
      ProgressionCache.Instance[identifier] = comp;
      
      Messenger.Default.Send(new CachesNeedRebuildEvent());
      
      try {
        progression.ApplyTo(comp);
      } catch (Exception e) {
        Log.Error(e, "Failed to apply progression {id}", identifier);
        GameObject.Destroy(comp.gameObject);
      }
    }
  }

  public PatchState Dump(string path = "")
  {
    try {
      var state = new PatchState
      {
        Progressions = UnityEngine.Object.FindObjectsByType<Progression>(UnityEngine.FindObjectsSortMode.None)
          .ToDictionary(p => p.identifier, p => new SerializedProgression(p)),
        MapFeatures = UnityEngine.Object.FindObjectsByType<MapFeature>(UnityEngine.FindObjectsSortMode.None)
          .ToDictionary(mf => mf.identifier, mf => new SerializedMapFeature(mf)),
      };
      if (path != "") {
        var jsonSerializerSettings = new JsonSerializerSettings
        {
          ContractResolver = new DefaultContractResolver
          {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
          },
        };
        var jsonSerializer = JsonSerializer.CreateDefault(jsonSerializerSettings);
        var obj = JObject.FromObject(state, jsonSerializer);
        File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented, jsonSerializerSettings));
      }

      return state;
    } catch (System.Exception e) {
      Log.Error(e, "Failed to dump progression");
    }

    return new PatchState();
  }
}