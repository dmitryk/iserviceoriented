﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Runtime.Serialization;

namespace IServiceOriented.ServiceBus
{
    /// <summary>
    /// Used to specify a handler for unhandled messages of a certain type
    /// </summary>
    [DataContract]
    public class UnhandledMessageFilter : TypedMessageFilter
    {
        public UnhandledMessageFilter(Type messageType)
            : base(messageType)
        {
        }
        protected internal override string CreateInitString()
        {
            return null;            
        }

        protected internal override void InitFromString(string data)
        {
            
        }

        public override bool Include(string action, object message)
        {
            return base.Include(action, message);
        }
    }
}