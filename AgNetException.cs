using System;
using System.Collections.Generic;
using System.Text;

namespace AgNet
{
    class AgNetException : Exception
    {
        public AgNetException()
        {
        }

        public AgNetException(string message)
            : base(message)
        {
        }
    }
}
