﻿namespace Lime.Transport.Http
{
    internal static class Constants
    {
        public const int DEFAULT_REQUEST_TIMEOUT = 60;
        public const int DEFAULT_TRANSPORT_EXPIRATION_INACTIVITY_INTERVAL = 180;
        public const string ROOT = "/";
        public const string MESSAGES_PATH = "messages";
        public const string COMMANDS_PATH = "commands";
        public const string NOTIFICATIONS_PATH = "notifications";
        public const string HTTP_METHOD_GET = "GET";
        public const string HTTP_METHOD_POST = "POST";
        public const string HTTP_METHOD_DELETE = "DELETE";
        public const string TEXT_PLAIN_HEADER_VALUE = "text/plain";
        public const string SESSION_ID_HEADER = "X-Session-Id";
        public const string SESSION_HEADER = "X-Session";
        public const string KEEP_ALIVE_HEADER_VALUE = "Keep-Alive";
        public const string CLOSE_HEADER_VALUE = "Close";
        public const string SESSION_EXPIRATION_HEADER = "X-Session-Expiration";
        public const string ENVELOPE_ID_HEADER = "X-Id";
        public const string ENVELOPE_ID_QUERY = "id";
        public const string ENVELOPE_FROM_HEADER = "X-From";
        public const string ENVELOPE_FROM_QUERY = "from";
        public const string ENVELOPE_TO_HEADER = "X-To";
        public const string ENVELOPE_TO_QUERY = "to";
        public const string ENVELOPE_PP_HEADER = "X-Pp";
        public const string ENVELOPE_PP_QUERY = "pp";
        public const string WAIT_UNTIL_QUERY = "waitUntil";
        public const string REASON_CODE_HEADER = "X-Reason-Code";
        public const string REASON_DESCRIPTION_HEADER = "X-Reason-Description";
        
    }
}
