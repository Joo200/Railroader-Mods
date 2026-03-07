using Game.Progression;

namespace AlinasMapMod.Caches;

public class SectionCache : ComponentCache<SectionCache, Section>
{
  
  public static string GetSectionIdentifier(string progressionId, string sectionId) => progressionId.ToLower() + "-" + sectionId.ToLower();
  public static string GetSectionIdentifier(Section obj) => GetSectionIdentifier(obj.GetComponentInParent<Progression>().identifier, obj.identifier);
  
  public override string GetIdentifier(Section obj) => GetSectionIdentifier(obj);
}
