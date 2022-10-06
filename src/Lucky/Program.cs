const string MCR_ADDRESS = "mcr.microsoft.com";
const string GHCR_ADDRESS = "ghcr.io";
const string DH_ADDRESS = "library";
const string MANIFEST_LIST_HEADER = "application/vnd.docker.distribution.manifest.list.v2+json";
const string MANIFEST_HEADER = "application/vnd.docker.distribution.manifest.v2+json";
const string API_VERSION = "v2";
const char TAG_CHAR = ':';

HttpClient client = new HttpClient();

var image = "mcr.microsoft.com/dotnet/samples:aspnetapp";
var baseImage = "mcr.microsoft.com/dotnet/aspnet:7.0";


void GetImageForString(string image)
{
    string registry;
    if (image.StartsWith(MCR_ADDRESS))
    {
        registry = MCR_ADDRESS;
    }
    else if (registry.StartsWith(GHCR_ADDRESS))
    {
        registry = GHCR_ADDRESS;
    }

    int tagStart = image.IndexOf(TAG_CHAR);
    int end = tagStart > 0 ? tagStart : image.Length - 1;

    if (tagStart)

}

void GetImageKind()
{

}

void RequestImageFromRegistry(string image)
{
    string url = $"https://{MCR_ADDRESS}/{API_VERSION}/{image}";
    client.GetStringAsync("");
}


/* 
curl -s -H "Accept: application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json" https://mcr.microsoft.com/v2/dotnet/samples/manifests/aspnetapp | jq

curl https://ghcr.io/token\?scope\="repository:richlander/dotnet-docker:pull"
{"token":"djE6cmljaGxhbmRlci9kb3RuZXQtZG9ja2VyOjE2NjQ5MjQ3NjkwMTgyNTg1Mjk="}
root@DESKTOP-NMDCRDH:~# curl -H "Authorization: Bearer djE6cmljaGxhbmRlci9kb3RuZXQtZG9ja2VyOjE2NjQ5MjQ3NjkwMTgyNTg1Mjk=" https://ghcr.io/v2/richlander/dotnet-docker/aspnetapp/manifests/main

*/
 struct Image
 {
    string Address;
    string Repo;
    string Tag;
    string Architecture;
    string Os;
 }
