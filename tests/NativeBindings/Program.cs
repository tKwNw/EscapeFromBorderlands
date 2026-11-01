using System.Text.Json;
var r=NativeBindings.Validate(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]),args.Length>3?Path.GetFullPath(args[3]):null);
var json=JsonSerializer.Serialize(r,new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args[2],json); Console.WriteLine(json);
return JsonSerializer.SerializeToElement(r).GetProperty("failed").GetInt32()==0 ? 0 : 1;
