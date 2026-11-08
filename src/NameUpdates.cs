using System;using System.Linq;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 bool UpdateOfficialName(WallpaperItem item,ImportCandidate candidate){if(candidate.NameSource==null||candidate.NameSource.Id!=candidate.Id||OfficialNames.Clean(candidate.NameSource.Name)!=candidate.Name)return false;return OfficialNameStore.Update(item,candidate.NameSource,Write);}
 readonly HashSet<string> nameDecisionsLogged=new HashSet<string>();
 void LogNameDecision(string phase,object data){if(phase=="official-name-kept"&&!nameDecisionsLogged.Add(Common.Json.Serialize(data)))return;Write(phase,data);}
 void ApplyOfficialNames(List<ImportCandidate> candidates){ObserveNameCandidates(candidates);if(OfficialNameStore.ApplyCards(library,new Dictionary<string,NameEvidence>(),candidates,LogNameDecision)>0)RefreshNameLabels();}
}
