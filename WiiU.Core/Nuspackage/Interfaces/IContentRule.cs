using WiiU.Core.Nuspackage.Packaging;

namespace WiiU.Core.Nuspackage.Interfaces
{
    public interface IContentRule
    {
        string GetPattern();
        void SetPattern(string pattern);

        ContentDetails GetDetails();
        void SetDetails(ContentDetails details);

        bool IsContentPerMatch();
        void SetContentPerMatch(bool contentPerMatch);
    }
}
