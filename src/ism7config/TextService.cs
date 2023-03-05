using LuCon.Common.ConfigService;
using LuCon.Common.Declarations;

namespace ism7config;

public class TextService : ITextService
{
    public string getLocalizedText(Language lang, string original, params object[] objects)
    {
        return String.Format(original, objects);
    }
}