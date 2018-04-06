#if NET462
extern alias aliastest;

namespace Test.ConsoleApp
{
    class AliasTest
    {
        public const string DataSet = nameof(aliastest::System.Data.DataSet);
    }
}
#endif
