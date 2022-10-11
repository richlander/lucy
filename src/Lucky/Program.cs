using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using static System.Linq.Enumerable;
using static System.Console;

// const string MCR_ADDRESS = "mcr.microsoft.com";
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
const string ERROR = "Something went wrong.";

/*
**CLI args**
Required:
[image] [string]
[baseImage] [string]

Optional:
--imageOs [string]
--imageArch [string]
--imageToken [string]
--baseImageToken [string]
--verbosity [int] 0-2
*/

var targetTag = string.Empty;
var baseTag = string.Empty;
// tokens are needed for private repos
// GHRC also requires tokens for public repos
string? targetToken = null;
string? baseToken = null;
int verboseLevel = 0;

// args = new string[]
// {
//     "mcr.microsoft.com/dotnet/samples:aspnetapp",
//     "mcr.microsoft.com/dotnet/aspnet:7.0",
// };

if (args.Length >= 2)
{
    targetTag = args[0];
    baseTag = args[1];
}
else
{
    WriteLine("Incorrect arguments were provided.");
    WriteHelp();
    return;
}

if (args.Length > 2)
{
    targetToken = GetArg("--imagetoken");
    baseToken = GetArg("--baseimagetoken");
    string? verbosity = GetArg("--verbosity");
    if (verbosity is not null)
    {
        int.TryParse(verbosity, out verboseLevel);
    }
}

HttpClient client = new HttpClient();
Platform DefaultPlatform = new Platform("amd64" ,"linux");
bool verbose = verboseLevel > 0;
bool isImageFresh = false;

if (verbose)
{
    WriteLine("Layer Update Check Yeoman v1.0");
    WriteLine($"Image: {targetTag}");
    WriteLine($"base: {baseTag}");
    WriteLine();
}

// Split the image address into components
Image targetImage = GetImageForAddress(targetTag);
targetImage.Token = targetToken;
// Get a token from GHRC if one is missing
// Assumes that the image is public; otherwise, this will fail
if (targetImage.Registry is GHCR_ADDRESS &&
    targetImage.Token is null)
{
    targetImage.Token = await GetGhcrToken(targetImage);
}

ImageManifest targetImageManifest = await RequestImageFromRegistry(targetImage);

Image baseImage = GetImageForAddress(baseTag);
ImageManifest baseImageManifest = await RequestImageFromRegistry(baseImage);
baseImage.Token = baseToken;

if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_HEADER)
{
    if (verbose)
    {
        WriteLine($"Both images type: {MANIFEST_HEADER}");
    }

    isImageFresh = IsLayersMatch(baseImageManifest, targetImageManifest);
}
else if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.Manifests is not null)
{
    if (verbose)
    {
        WriteLine($"Image is type: {MANIFEST_HEADER}");
        WriteLine($"Base Image is type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Validate for: {DefaultPlatform.Os}/{DefaultPlatform.Architecture}");
    } 

    Manifest? baseShaManifest = GetManifestFlavor(baseImageManifest.Manifests, DefaultPlatform);

    if (baseShaManifest is null)
    {
        WriteLine($"Platform manifest cannot be found in base image: {DefaultPlatform}");
    }
    else
    {
        baseImage.Digest = baseShaManifest.Digest;
        ImageManifest baseShaImageManifest = await RequestImageFromRegistry(baseImage);

        isImageFresh = IsLayersMatch(baseShaImageManifest, targetImageManifest);
    }
}
else if (targetImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    targetImageManifest.Manifests is not null &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.Manifests is not null)
{
    if (verbose)
    {
        WriteLine($"Both images type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Image includes {targetImageManifest.Manifests.Count} manifests that will be validated.");
    }
    // Iterate over target manifests and then compare with baseimage manifests
    foreach (var targetManifest in targetImageManifest.Manifests)
    {
        targetImage.Digest = targetManifest.Digest;
        var targetShaImageManfest = await RequestImageFromRegistry(targetImage);
        Manifest? baseShaManifest = GetManifestFlavor(baseImageManifest.Manifests, targetManifest.Platform);

        if (baseShaManifest is null)
        {
            WriteLine($"Platform manifest cannot be found in base image: {targetManifest.Platform}");
            break;
        }

        baseImage.Digest = baseShaManifest.Digest;
        var baseShaImageManfest = await RequestImageFromRegistry(baseImage);

        if (verboseLevel > 1)
        {
            WriteLine();
            string version = targetManifest.Platform.OsVersion is object ? $":{targetManifest.Platform.OsVersion}" : string.Empty;
            WriteLine($"Validate for: {targetManifest.Platform.Os}{version}/{targetManifest.Platform.Architecture}");
        }

        bool match = IsLayersMatch(baseShaImageManfest, targetShaImageManfest);
        if (!match)
        {
            isImageFresh = match;
            break;
        }
    }

    isImageFresh = true;
}
else
{
    throw new Exception(ERROR);
}

WriteLine();
WriteLine("Image is fresh:");
WriteLine(isImageFresh);

bool IsLayersMatch(ImageManifest lower, ImageManifest higher)
{
    if (lower.Layers is null or [] ||
        higher.Layers is null or [])
    {
        throw new Exception(ERROR);
    }

    foreach (int index in Range(0, lower.Layers.Count))
    {
        if (lower.Layers[index].Digest != higher.Layers[index].Digest)
        {
            if (verbose)
            {
                WriteLine($"Layer {index} doesn't match.");
                WriteLine($"Base image layer: {lower.Layers[index].Digest}");
                WriteLine($"Image layer: {higher.Layers[index].Digest}");
            }
            return false;
        }

        if (verboseLevel > 1)
        {
            WriteLine($"Layer match: {lower.Layers[index].Digest}");
        }

    }

    return true;
}

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

    Image image = new(address, registry, repo, tag);

    return image;
}

Manifest? GetManifestFlavor(List<Manifest> manifestList, Platform platform) => manifestList.FirstOrDefault((m) => m.Platform == platform);

async Task<ImageManifest> RequestImageFromRegistry(Image image)
{
    string tag = image.Digest ?? image.Tag;
    string url = $"https://{image.Registry}/{API_VERSION}/{image.Repo}/{MANIFESTS}/{tag}";
    
    using HttpRequestMessage request = new(HttpMethod.Get, url);
    request.Headers.Accept.Add(GetHeader(MANIFEST_LIST_HEADER));
    request.Headers.Accept.Add(GetHeader(MANIFEST_HEADER));

    if (!string.IsNullOrEmpty(image.Token))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", image.Token);
    }

    var response = await client.SendAsync(request);

    ImageManifest? manifest = null;

    if (response.IsSuccessStatusCode)
    {
        manifest = await response.Content.ReadFromJsonAsync<ImageManifest>();
    }

    return manifest ?? throw new Exception($"Registry responded with {response.StatusCode} and message: {response.RequestMessage}");
}

MediaTypeWithQualityHeaderValue GetHeader(string value) => new(new(value), 0.5);

async Task<string> GetGhcrToken(Image image)
{
    string url = $"https://{GHCR_ADDRESS}/token?scope=repository:{image.Repo}:pull";
    RegistryToken? token = await client.GetFromJsonAsync<RegistryToken>(url);
    return token?.Token ?? throw new Exception("Registry did not return valid token. Consider providing a token.");
}

string? GetArg(string arg)
{
    foreach (int index in Range(2, args.Length - 2))
    {
        bool last = index == args.Length - 1;
        if (arg == args[index].ToLowerInvariant() &&
        !last)
        {
            return args[index + 1];
        }
    }

    return null;
}

void WriteHelp()
{
WriteLine("""
**CLI args**
Required:
[image] [string]
[baseImage] [string]

Optional:
--imageOs [string]
--imageArch [string]
--imageToken [string]
--baseImageToken [string]
--verbosity [int] 0-2
""");
}

record RegistryToken(string? Token);

record Image(string? Address, string Registry, string Repo, string Tag)
{
    public string? Digest { get; set; }
    public string? Token { get; set; }
    public string? Architecture { get; set; }
    public string? Os { get; set; }
};

record ImageManifest(int SchemaVersion, string MediaType)
{
    public List<Layer>? Layers { get; set; }
    public List<Manifest>? Manifests { get; set; }
};

record Layer(string MediaType, string Digest);

record Manifest(string MediaType, string Digest, Platform Platform);

record Platform(string Architecture, string Os)
{
    [JsonPropertyName("os.version")]
    public string? OsVersion { get; set; }
};
