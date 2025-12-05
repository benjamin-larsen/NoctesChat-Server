using MySqlConnector;

namespace NoctesChat;

public class Database {
    public static SnowflakeGen UserIDGenerator = new (1735689600000);
    public static SnowflakeGen MsgIDGenerator = new (1735689600000);
    public static SnowflakeGen ChannelIDGenerator = new (1735689600000);
    
    static string connectionUrl = Environment.GetEnvironmentVariable("db_conn")!;

    public static void Setup() {
        using var conn = new MySqlConnection(connectionUrl);
        conn.Open();
        
        using var cmd = conn.CreateCommand()!;
        
        // Setup Database
        cmd.CommandText = "CREATE DATABASE IF NOT EXISTS `noctes_chat` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;";
        cmd.ExecuteNonQuery();
        conn.ChangeDatabase("noctes_chat");

        // Setup Users Table
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS `users` (
                            `id` bigint unsigned NOT NULL,
                            `username` varchar(20) NOT NULL,
                            `email` varchar(254) NOT NULL,
                            `email_verified` bool NOT NULL DEFAULT false,
                            `password_hash` binary(32) NOT NULL,
                            `password_salt` binary(16) NOT NULL,
                            `created_at` bigint NOT NULL,
                            PRIMARY KEY (`id`),
                            UNIQUE KEY `users_username` (`username`),
                            UNIQUE KEY `users_email` (`email`)
                          )
                          """;
        cmd.ExecuteNonQuery();
        
        // Setup User Tokens Table
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS `user_tokens` (
                            `user_id` bigint unsigned NOT NULL,
                            `key_hash` binary(32) NOT NULL,
                            `created_at` bigint NOT NULL,
                            PRIMARY KEY (`user_id`,`key_hash`),

                            CONSTRAINT `fk_user_tokens`
                              FOREIGN KEY (`user_id`) REFERENCES `users` (`id`)
                                ON DELETE CASCADE
                          )
                          """;
        cmd.ExecuteNonQuery();
        
        // Setup Channels Table
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS `channels` (
                            `id` bigint unsigned NOT NULL,
                            `owner` bigint unsigned NOT NULL,
                            `name` varchar(50) NOT NULL,
                            `member_count` int unsigned NOT NULL,
                            `created_at` bigint NOT NULL,
                            PRIMARY KEY (`id`),
                            KEY `fk_channel_owner` (`owner`),

                            CONSTRAINT `fk_channel_owner`
                                FOREIGN KEY (`owner`) REFERENCES `users` (`id`)
                                    ON DELETE RESTRICT
                          )
                          """;
        cmd.ExecuteNonQuery();
        
        // Setup Channel Members Table
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS `channel_members` (
                            `user_id` bigint unsigned NOT NULL,
                            `channel_id` bigint unsigned NOT NULL,
                            `last_accessed` bigint NOT NULL,
                            PRIMARY KEY (`user_id`,`channel_id`),
                            KEY `fk_channel_members` (`channel_id`),

                            CONSTRAINT `fk_channel_members`
                              FOREIGN KEY (`channel_id`) REFERENCES `channels` (`id`)
                                ON DELETE CASCADE,

                            CONSTRAINT `fk_user_channels`
                              FOREIGN KEY (`user_id`) REFERENCES `users` (`id`)
                                ON DELETE CASCADE
                          )
                          """;
        cmd.ExecuteNonQuery();

        // Setup Messages Table
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS `messages` (
                            `id` bigint unsigned NOT NULL,
                            `channel_id` bigint unsigned NOT NULL,
                            `author_id` bigint unsigned DEFAULT NULL,
                            `content` text NOT NULL,
                            `timestamp` bigint NOT NULL,
                            `edited_timestamp` bigint DEFAULT NULL,
                            PRIMARY KEY (`id`),
                            KEY `fk_message_author` (`author_id`),
                            KEY `index_channel_messages` (`channel_id`,`id`),

                            CONSTRAINT `fk_message_channel`
                              FOREIGN KEY (`channel_id`) REFERENCES `channels` (`id`)
                              ON DELETE CASCADE,

                            CONSTRAINT `fk_message_author`
                              FOREIGN KEY (`author_id`) REFERENCES `users` (`id`)
                                ON DELETE SET NULL
                          )
                          """;
        cmd.ExecuteNonQuery();

        // BUG: If deleted by cascade, will not call trigger.
        // Setup trigger for decreasing member count
        cmd.CommandText = "DROP TRIGGER IF EXISTS `member_count_delete`";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = """
                          CREATE TRIGGER `member_count_delete`
                              AFTER DELETE ON `channel_members`
                              FOR EACH ROW
                                UPDATE channels
                                SET member_count = member_count - 1
                                WHERE id = old.channel_id and member_count > 0;
                          """;
        cmd.ExecuteNonQuery();
        
        // Setup trigger for increasing memeber count
        cmd.CommandText = "DROP TRIGGER IF EXISTS `member_count_insert`";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = """
                          CREATE TRIGGER `member_count_insert`
                              AFTER INSERT ON `channel_members`
                              FOR EACH ROW
                                UPDATE channels
                                SET member_count = member_count + 1
                                WHERE id = new.channel_id;
                          """;
        cmd.ExecuteNonQuery();
    }

    public static async Task<bool> HasUserToken(ulong userId, byte[] tokenHash, CancellationToken ct) {
        await using var conn = await Database.GetConnection(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM user_tokens WHERE user_id = @id AND key_hash = @key_hash;";

        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@key_hash", tokenHash);

        var value = await cmd.ExecuteScalarAsync(ct);
        
        return value != null;
    }

    public static async Task<bool> InsertUser(
        User user, MySqlConnection conn, MySqlTransaction transaction, CancellationToken ct) {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
                          INSERT INTO users (
                            id,
                            username,
                            email,
                            email_verified,
                            password_hash,
                            password_salt,
                            created_at
                          )
                          VALUES(
                            @id,
                            @username,
                            @email,
                            @email_verified,
                            @password_hash,
                            @password_salt,
                            @created_at
                          );
                          """;

        cmd.Parameters.AddWithValue("@id", user.ID);
        cmd.Parameters.AddWithValue("@username", user.Username);
        cmd.Parameters.AddWithValue("@email", user.Email);
        cmd.Parameters.AddWithValue("@email_verified", user.EmailVerified);
        cmd.Parameters.AddWithValue("@password_hash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@password_salt", user.PasswordSalt);
        cmd.Parameters.AddWithValue("@created_at", user.CreatedAt);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        
        return rowsAffected == 1;
    }
    
    public static async Task<bool> InsertUserToken(
        ulong userId,
        byte[] keyHash,
        long createdAt,
        MySqlConnection conn,
        MySqlTransaction transaction,
        CancellationToken ct) {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
                          INSERT INTO user_tokens(user_id, key_hash, created_at)
                          VALUES(@user_id, @key_hash, @created_at);
                          """;

        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@key_hash", keyHash);
        cmd.Parameters.AddWithValue("@created_at", createdAt);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
        
        return rowsAffected == 1;
    }

    public static async Task<User?> GetUserById(ulong userId, bool includeEmail, CancellationToken ct) {
        await using var conn = await Database.GetConnection(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
                          SELECT
                              id, username{(includeEmail ? ", email, email_verified" : "")}, created_at
                          FROM users WHERE id = @id;
                          """;

        cmd.Parameters.AddWithValue("@id", userId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) return null;

        var field = 0;

        return new User {
            ID = reader.GetUInt64(field++),
            Username = reader.GetString(field++),
            Email = includeEmail ? reader.GetString(field++) : null,
            EmailVerified = includeEmail && reader.GetBoolean(field++),
            CreatedAt = reader.GetFieldValue<long>(field),
        };
    }
    
    public static async Task<User?> GetUserForLogin(
        string email, MySqlConnection conn, MySqlTransaction transaction, CancellationToken ct) {
        await using var cmd  = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
                           SELECT
                               id, password_hash, password_salt
                           FROM users WHERE email = @email
                           FOR UPDATE;
                           """;

        cmd.Parameters.AddWithValue("@email", email);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) return null;
        
        return new User {
            ID = reader.GetUInt64(0 /* id */),
            PasswordHash = reader.GetFieldValue<byte[]>(1 /* password_hash */),
            PasswordSalt = reader.GetFieldValue<byte[]>(2 /* password_salt */),
        };
    }

    public static async Task<bool> ExistsInChannel(
        ulong userId, ulong channelId, MySqlConnection conn, MySqlTransaction? transaction, CancellationToken ct) {
        await using var cmd  = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"SELECT 1 FROM channel_members WHERE user_id = @user_id AND channel_id = @channel_id;";
        
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@channel_id", channelId);

        var value = await cmd.ExecuteScalarAsync(ct);
        
        return value != null;
    }
    
    public static async Task<MySqlConnection> GetConnection(CancellationToken ct)
    {
        var conn = new MySqlConnection(connectionUrl + "Database=noctes_chat; UseAffectedRows=true;");
        await conn.OpenAsync(ct);
        return conn;
    }
}