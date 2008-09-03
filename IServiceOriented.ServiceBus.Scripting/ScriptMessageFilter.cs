﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Scripting;

using System.Runtime.Serialization;

namespace IServiceOriented.ServiceBus.Scripting
{
    [Serializable]
    [DataContract]
    public class ScriptMessageFilter : MessageFilter
    {
        public ScriptMessageFilter(string languageId, string code)
        {
            Script = new Script(languageId, code);
        }

        public ScriptMessageFilter(string languageId, string code, SourceCodeKind sourceCodeKind)
        {
            Script = new Script(languageId, code, sourceCodeKind);         
        }
        
        [DataMember]
        public Script Script
        {
            get;
            private set;
        }
        
        public override bool Include(PublishRequest request)
        {
            return (bool)Script.ExecuteWithVariables(new Dictionary<string, object>() { { "request", request } });
        }
    }
}
