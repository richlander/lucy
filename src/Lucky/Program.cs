using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Linq.Enumerable;

const string MCR_ADDRESS = "mcr.microsoft.com";
const string GHCR_ADDRESS = "ghcr.io";
const string DH_ADDRESS = "registry-1.docker.io";
const string MANIFEST_LIST_HEADER = "application/vnd.docker.distribution.manifest.list.v2+json";
const string MANIFEST_HEADER = "application/vnd.docker.distribution.manifest.v2+json";
const string API_VERSION = "v2";
const char TAG_CHAR = ':';
const char DOT_CHAR = '.';
const char SLASH_CHAR = '/';
const string LIBRARY = "library";
const string MANIFESTS = "manifests";
const string LATEST = "latest";
const string NULL = "null";

Platform DefaultPlatform = new Platform(){Architecture = "amd64", Os = "linux"};
HttpClient client = new HttpClient();
string targetToken = string.Empty;
string baseToken = string.Empty;

var targetTag = "mcr.microsoft.com/dotnet/samples:aspnetapp";
var baseTag = "mcr.microsoft.com/dotnet/aspnet:7.0";
var ghcrImage = "ghcr.io/richlander/dotnet-docker/aspnetapp:main";
// targetTag = ghcrImage;
// var dhImage = "debian";

Image targetImage = GetImageForAddress(targetTag);
targetImage.Token = targetToken;
if (targetImage.Registry is GHCR_ADDRESS &&
    string.IsNullOrEmpty(targetImage.Token))
{
    targetImage.Token = await GetGhcrToken(targetImage);
}

ImageManifest targetImageManifest = await RequestImageFromRegistry(targetImage);

Image baseImage = GetImageForAddress(baseTag);
ImageManifest baseImageManifest = await RequestImageFromRegistry(baseImage);

baseImage.Token = baseToken;
string[] layers;

if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_HEADER)
{
    bool matches = IsLayersMatch(baseImageManifest, targetImageManifest, out layers);
    Return(matches, layers);
}
else if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER)
{
    Manifest baseShaManifest = GetManifestFlavor(baseImageManifest, DefaultPlatform);
    baseImage.Tag = baseShaManifest.Digest;
    ImageManifest baseShaImageManifest = await RequestImageFromRegistry(baseImage);

    bool matches = IsLayersMatch(baseShaImageManifest, targetImageManifest, out layers);
    Return(matches, layers);
}
else if (targetImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER)
{
    bool matches = true;
    // Iterate over target manifests and then compare with baseimage manifests
    foreach (var targetManifest in targetImageManifest.Manifests)
    {
        var baseShaManifest = GetManifestFlavor(baseImageManifest, targetManifest.Platform);
        baseImage.Tag = baseShaManifest.Digest;
        var baseShaImageManfest = await RequestImageFromRegistry(baseImage);
        targetImage.Tag = targetManifest.Digest;
        var targetShaImageManfest = await RequestImageFromRegistry(targetImage);
        bool match = IsLayersMatch(baseShaImageManfest, targetShaImageManfest, out layers);
        if (match is false)
        {
            matches = match;
            break;
        }
    }
    Console.WriteLine(matches);
}
else
{
    throw new Exception("Something is wrong");
}

async Task<string> GetGhcrToken(Image image)
{
    var def = new {Token = string.Empty};
    string url = $"https://{GHCR_ADDRESS}/token?scope=repository:{image.Repo}:pull";
    var token = await client.GetFromJsonAsync<RegistryToken>(url);
    return token?.Token ?? string.Empty;
}

void Return(bool matches, string[] layers)
{
    Console.WriteLine(matches);
    Console.WriteLine();
    Console.WriteLine("Last layer:");
    Console.WriteLine($"Base: {layers[0]}");
    Console.WriteLine($"Image: {layers[1]}");
}

bool IsLayersMatch(ImageManifest lower, ImageManifest higher, out string[] lastLayer)
{
    lastLayer = new string[2];

    if (lower.Layers is null or [] ||
        higher.Layers is null or [])
    {
        return false;
    }

    foreach (int index in Range(0, lower.Layers.Count))
    {
        if (lower.Layers[index].Digest != higher.Layers[index].Digest)
        {
            lastLayer[0] = lower.Layers[index].Digest ?? NULL;
            lastLayer[1] = higher.Layers[index].Digest ?? NULL;
            return false;
        }
    }

    int last = lower.Layers.Count - 1;
    lastLayer[0] = lower.Layers[last].Digest ?? NULL;
    lastLayer[1] = higher.Layers[last].Digest ?? NULL;
    return true;
}

// IEnumerable<string> GetPlatforms(ImageManifest imageManifest)
// {
//     if (imageManifest.Manifests is null)
//     {
//         yield return string.Empty;
//     }

//     foreach (Manifest m in imageManifest.Manifests)
//     {
        
//     }
// }

Image GetImageForAddress(string address)
{
    int firstDot = address.IndexOf(DOT_CHAR);
    int firstSlash = address.IndexOf(SLASH_CHAR);
    string registry = string.Empty;
    string repo = string.Empty;

    int tagStart = address.IndexOf(TAG_CHAR);
    string tag = tagStart > 0 ? address.Substring(tagStart + 1) : string.Empty;

    if (firstDot > 0 && firstSlash > firstDot)
    {
        registry = address.Substring(0, firstSlash);
        repo = tagStart > 0 ? address.Substring(registry.Length + 1, tagStart - registry.Length - 1) : address.Substring(registry.Length + 1);
    }
    else if (address.StartsWith(LIBRARY))
    {
        registry = DH_ADDRESS;
        repo = tagStart > 0 ? address.Substring(LIBRARY.Length + 1, tagStart - LIBRARY.Length - 1) : address.Substring(LIBRARY.Length + 1);
    }
    else
    {
        registry = DH_ADDRESS;
        repo = tagStart > 0 ? address.Substring(0, tagStart) : address;
    }

    if (tag == string.Empty && registry is not GHCR_ADDRESS)
    {
        tag = LATEST;
    }

    Image image = new()
    {
        Address = address,
        Registry = registry,
        Repo = repo,
        Tag = tag
    };

    return image;
}

Manifest GetManifestFlavor(ImageManifest manifestList, Platform platform)
{
    foreach (Manifest manifest in manifestList.Manifests)
    {
        if (manifest.Platform.Architecture == platform.Architecture &&
            manifest.Platform.Os == platform.Os &&
            manifest.Platform.OsVersion == platform.OsVersion)
            {
                return manifest;
            }
    }

    return new Manifest();
}

async Task<ImageManifest> RequestImageFromRegistry(Image image)
{
    // string tag = string.IsNullOrEmpty(image.Tag) ? string.Empty : $":{image.Tag}";
    string url = $"https://{image.Registry}/{API_VERSION}/{image.Repo}/{MANIFESTS}/{image.Tag}";
    
    using HttpRequestMessage request = new(HttpMethod.Get, url);
    request.Headers.Accept.Add(GetHeader(MANIFEST_LIST_HEADER));
    request.Headers.Accept.Add(GetHeader(MANIFEST_HEADER));

    if (!string.IsNullOrEmpty(image.Token))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", image.Token);
    }

    var response = await client.SendAsync(request);

    JsonSerializerOptions options = new();
    ImageManifest? manifest = null;

    if (response.IsSuccessStatusCode && response.Content is not null)
    {
        manifest = await response.Content.ReadFromJsonAsync<ImageManifest>();
    }

    return manifest ?? new ImageManifest();
}

MediaTypeWithQualityHeaderValue GetHeader(string value) => new(new(value), 0.5);

class RegistryToken
{
    public string? Token { get; set; }
}

 class Image
 {
    public string? Address { get; set; }
    public string? Registry { get; set; }
    public string? Repo { get; set; }
    public string? Tag { get; set; }
    public string? Token { get; set; }
    public string? Architecture { get; set; }
    public string? Os { get; set; }
 }

class ImageManifest
{
    public int SchemaVersion { get; set; }
    public string? MediaType { get; set; }
    public List<Layer>? Layers { get; set; }
    public List<Manifest>? Manifests { get; set; }
}

class Layer
{
    public string? MediaType { get; set; }
    public string? Digest { get; set; }
}

class Manifest
{
    public string? MediaType { get; set; }
    public string? Digest { get; set; }
    public Platform? Platform { get; set; }
}

class Platform
{
    public string? Architecture { get; set; }
    public string? Os { get; set; }
    [JsonPropertyName("os.version")]
    public string? OsVersion { get; set; }
}
