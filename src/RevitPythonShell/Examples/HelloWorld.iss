[Files]
Source: Output_HelloWorld\RpsRuntime.dll; DestDir: {app};
Source: Output_HelloWorld\IronPython.dll; DestDir: {app};
Source: Output_HelloWorld\IronPython.Modules.dll; DestDir: {app};
Source: Output_HelloWorld\Microsoft.Scripting.Metadata.dll; DestDir: {app};
Source: Output_HelloWorld\Microsoft.Dynamic.dll; DestDir: {app};
Source: Output_HelloWorld\Microsoft.Scripting.dll; DestDir: {app};

; this is the main dll with the script embedded
Source: Output_HelloWorld\HelloWorld.dll; DestDir: {app};

; add a similar line, if your addin requires a configuration file (search paths or predefined variables)
;Source: HelloWorld.xml; DestDir: {userappdata}\HelloWorld; Flags: onlyifdoesntexist; 

[code]
{ install revit manifest file }
procedure CurStepChanged(CurStep: TSetupStep);
var
  AddInFilePath: String;
  AddInFileContents: String;
begin

  if CurStep = ssPostInstall then
  begin

  	{ GET LOCATION OF USER AppData (Roaming) }
  	AddInFilePath := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2020\HelloWorld.addin');
  
  	{ CREATE NEW ADDIN FILE }
  	AddInFileContents := '<?xml version="1.0" encoding="utf-8" standalone="no"?>' + #13#10;
  	AddInFileContents := AddInFileContents + '<RevitAddIns>' + #13#10;
  	AddInFileContents := AddInFileContents + '  <AddIn Type="Application">' + #13#10;
    AddInFileContents := AddInFileContents + '    <Name>HelloWorld</Name>' + #13#10;
  	AddInFileContents := AddInFileContents + '    <Assembly>'  + ExpandConstant('{app}') + '\HelloWorld.dll</Assembly>' + #13#10;
  	
  	{ NOTE: create your own GUID here!!! }
  	AddInFileContents := AddInFileContents + '    <AddInId>276D41F2-CCC4-4B55-AF2A-47D30227F289</AddInId>' + #13#10;
  	
  	AddInFileContents := AddInFileContents + '    <FullClassName>HelloWorld</FullClassName>' + #13#10;
  	
  	{ NOTE: you should register your own VendorId with Autodesk }
  	AddInFileContents := AddInFileContents + '  <VendorId>RIPS</VendorId>' + #13#10;
  	AddInFileContents := AddInFileContents + '  </AddIn>' + #13#10;
  	AddInFileContents := AddInFileContents + '</RevitAddIns>' + #13#10;
  	SaveStringToFile(AddInFilePath, AddInFileContents, False);

  end;
end;

{ uninstall revit addin manifest }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AddInFilePath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AddInFilePath := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2020\HelloWorld.addin');
    
    if FileExists(AddInFilePath) then
    begin
      DeleteFile(AddInFilePath);
    end;
  end;
end;


[Setup]
AppName=HelloWorld
AppVerName=HelloWorld
RestartIfNeededByRun=false
DefaultDirName={pf32}\HelloWorld
OutputBaseFilename=Setup_HelloWorld
ShowLanguageDialog=auto
FlatComponentsList=false
UninstallFilesDir={app}\Uninstall
UninstallDisplayName=HelloWorld
AppVersion=2012.0
VersionInfoVersion=2012.0
VersionInfoDescription=HelloWorld
VersionInfoTextVersion=HelloWorld

