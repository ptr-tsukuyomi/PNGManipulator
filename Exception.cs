using System;
using System.Collections.Generic;
using System.Text;

namespace PNGManipulator
{
    public class InvalidPNGException : Exception
    {
        public InvalidPNGException(string msg, Exception inner = null) : base(msg, inner) { }
    }
}
