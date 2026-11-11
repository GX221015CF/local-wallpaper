using System;
using System.Linq;
using System.Collections.Generic;

internal static class ImportCandidateSearch {
 internal static string Key(ImportCandidate candidate) {
  return (candidate.Record ?? "") + "|" + (candidate.Source ?? "") + "|" + candidate.Id;
 }
 internal static string[] Aliases(ImportCandidate candidate, IEnumerable<WallpaperItem> library) {
  return library.Where(item => OfficialNameStore.ItemId(item) == candidate.Id)
   .OrderByDescending(item => item.CustomName != null)
   .SelectMany(item => new[] { item.Name, item.CustomName, item.NameBeforeCustom,
    item.NameSource != null && OfficialNameStore.Valid(item.NameSource, candidate.Id) ? item.NameSource.Name : null })
   .Where(name => !String.IsNullOrWhiteSpace(name)).Distinct().ToArray();
 }
 internal static bool Matches(ImportCandidate candidate, string query, IEnumerable<WallpaperItem> library) {
  query = (query ?? "").Trim();
  return query.Length == 0 || Contains(candidate.Id, query) || Contains(candidate.Name, query)
   || Aliases(candidate, library).Any(name => Contains(name, query));
 }
 static bool Contains(string text, string query) {
  return (text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
 }
}
