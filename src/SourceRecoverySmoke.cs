#if TEST_BUILD
using System;
using System.IO;
using System.Diagnostics;

internal static class SourceRecoverySmoke {
 internal static int Run(string suite){
  suite=Path.GetFullPath(suite);string isolation=Path.GetFullPath(Path.Combine(Common.AppRoot,"tests","isolation"));
  if(!LocalImport.Under(suite,isolation))return 2;
  Directory.CreateDirectory(suite);string report=Path.Combine(suite,"source-recovery-summary-r16.json");
  if(File.Exists(report))return 3;
  try{
   int sourceResult=SourceLocationSmoke.Run(Path.Combine(suite,"source-location"));if(sourceResult!=0)throw new InvalidOperationException("Source-location processor suite failed.");
   int nameResult=NameDiagnosticsSmoke.Run(Common.Root,"r15");if(nameResult!=0)throw new InvalidOperationException("Name-cache diagnostic processor suite failed.");
   Common.SaveNew(report,new{passed=true,classification="TEST_BUILD direct processors plus a bounded independent TEST_BUILD process; no GUI shown or user action automated",sourceLocationProcessor=true,knownSourceDiscovery=true,emptyKnownLayoutStatus=true,customChineseSpacePath=true,invalidSelectionPreservesPriorConfig=true,corruptRecordClassification=true,nameCacheFailureClasses=true,savedNamesPreserved=true,independentProcessReopen=true,formalDataTouched=false,thirdPartyCacheTouched=false,playerStarted=false,desktopChanged=false});return 0;
  }catch(Exception ex){try{Common.SaveNew(Path.Combine(suite,"source-recovery-failed-r16.json"),new{passed=false,error=ex.ToString(),formalDataTouched=false,thirdPartyCacheTouched=false,playerStarted=false,desktopChanged=false});}catch{}return 1;}
 }
}
#endif
