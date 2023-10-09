using Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TServer.Services
{
    public class TServerService_LServer: TServerLServerService.TServerLServerServiceBase
    {

        private readonly TServerService tServerService;

        public TServerService_LServer (TServerService tServerService)
        {
            this.tServerService = tServerService;
        }
    }
}
