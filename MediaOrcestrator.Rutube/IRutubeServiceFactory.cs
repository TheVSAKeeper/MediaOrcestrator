namespace MediaOrcestrator.Rutube;

public interface IRutubeServiceFactory
{
    RutubeService Create(string cookieString, string csrfToken);
}
