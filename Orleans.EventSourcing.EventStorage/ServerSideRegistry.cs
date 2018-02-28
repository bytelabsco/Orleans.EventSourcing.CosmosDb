namespace Orleans.EventSourcing.EventStorage.StoredProcedures
{
    using System.Collections.Generic;

    public static class ServerSideRegistry
    {
        public static class StoredProcedures
        {
            public static string Commit { get { return "commit"; } }
            public static string BumpCommit { get { return "bumpCommit"; } }

            public static Dictionary<string, string> Files
            {
                get
                {
                    return new Dictionary<string, string>
                    {
                        { Commit, "commit.js" },
                        { BumpCommit, "bump-commit.js" }
                    };
                }
            }
        }

        public static class PreTriggers
        {
            public static string JsonParse { get { return "jsonParse"; } }

            public static Dictionary<string, string> Files
            {
                get
                {
                    return new Dictionary<string, string>
                    {
                        { JsonParse, "json-parse-pretrigger.js" }
                    };
                }
            }
        }

        public static class PostTriggers
        {
            public static string JsonStringify { get { return "jsonStringify"; } }

            public static Dictionary<string, string> Files
            {
                get
                {
                    return new Dictionary<string, string>
                    {
                        { JsonStringify, "json-stringify-posttrigger.js" }
                    };
                }
            }
        }
    }
}
