/// <summary>
/// Default download sets ported from RomGoGetter (RomGoGetter_groups.json), archive.org only, for the
/// disc/large consoles the catalog syncs (psx/ps2/ps3/psp/xbox/xbox360/n3ds/nds — RomGoGetter ships
/// no archive.org sets for the cart systems; Wii U is intentionally excluded, reserved for the
/// clean-room NUS path). Values are archive.org item identifiers; the full <c>/download/</c> URL is
/// built by <see cref="Links"/> at seed time. Seeded once on startup.
/// </summary>
static class DefaultSets
{
    public static readonly IReadOnlyList<(string Name, string Console, string[] Items)> All =
    [
        ("PS1 Archive", "psx", [
            "sony_playstation_part1", "sony_playstation_part2", "sony_playstation_part3",
            "sony_playstation_part4", "sony_playstation_part5",
        ]),
        ("PS2 Archive", "ps2", [
            "sony_playstation2_numberssymbols", "sony_playstation2_a", "sony_playstation2_b",
            "sony_playstation2_c", "sony_playstation2_d_part1", "sony_playstation2_d_part2",
            "sony_playstation2_e", "sony_playstation2_f", "sony_playstation2_g", "sony_playstation2_h",
            "sony_playstation2_i", "sony_playstation2_j", "sony_playstation2_k", "sony_playstation2_l",
            "sony_playstation2_m_part1", "sony_playstation2_m_part2", "sony_playstation2_n",
            "sony_playstation2_o_part1", "sony_playstation2_o_part2", "sony_playstation2_p",
            "sony_playstation2_q", "sony_playstation2_r", "sony_playstation2_s_part1",
            "sony_playstation2_s_part2", "sony_playstation2_s_part3", "sony_playstation2_s_part4",
            "sony_playstation2_t", "sony_playstation2_u", "sony_playstation2_v", "sony_playstation2_w",
            "sony_playstation2_x", "sony_playstation2_z",
        ]),
        ("PS3 Archive", "ps3", [
            "sony_playstation3_numberssymbols",
            "sony_playstation3_a_part1", "sony_playstation3_a_part2", "sony_playstation3_a_part3",
            "sony_playstation3_b_part1", "sony_playstation3_b_part2", "sony_playstation3_b_part3",
            "sony_playstation3_c_part1", "sony_playstation3_c_part2", "sony_playstation3_c_part3",
            "sony_playstation3_d_part1", "sony_playstation3_d_part2", "sony_playstation3_d_part3",
            "sony_playstation3_d_part4", "sony_playstation3_d_part5", "sony_playstation3_e",
            "sony_playstation3_f_part1", "sony_playstation3_f_part2", "sony_playstation3_f_part3",
            "sony_playstation3_g_part1", "sony_playstation3_g_part2", "sony_playstation3_g_part3",
            "sony_playstation3_h_part1", "sony_playstation3_h_part2", "sony_playstation3_i",
            "sony_playstation3_j", "sony_playstation3_k", "sony_playstation3_l_part1",
            "sony_playstation3_l_part2", "sony_playstation3_l_part3", "sony_playstation3_m_part1",
            "sony_playstation3_m_part2", "sony_playstation3_m_part3", "sony_playstation3_m_part4",
            "sony_playstation3_m_part5", "sony_playstation3_n_part1", "sony_playstation3_n_part2",
            "sony_playstation3_n_part3", "sony_playstation3_o_part1", "sony_playstation3_o_part2",
            "sony_playstation3_o_part3", "sony_playstation3_p_part1", "sony_playstation3_p_part2",
            "sony_playstation3_q", "sony_playstation3_r_part1", "sony_playstation3_r_part2",
            "sony_playstation3_r_part3", "sony_playstation3_r_part4", "sony_playstation3_s_part1",
            "sony_playstation3_s_part2", "sony_playstation3_s_part3", "sony_playstation3_s_part4",
            "sony_playstation3_s_part5", "sony_playstation3_s_part6", "sony_playstation3_t_part1",
            "sony_playstation3_t_part2", "sony_playstation3_t_part3", "sony_playstation3_t_part4",
            "sony_playstation3_u_part1", "sony_playstation3_u_part2", "sony_playstation3_v",
            "sony_playstation3_w_part1", "sony_playstation3_w_part2", "sony_playstation3_x",
            "sony_playstation3_y", "sony_playstation3_z",
        ]),
        ("PSP Archive", "psp", ["psp_20220507", "psp_20220507_2"]),
        ("Xbox Archive", "xbox", [
            "microsoft_xbox_numberssymbols", "microsoft_xbox_a", "microsoft_xbox_b",
            "microsoft_xbox_c_part1", "microsoft_xbox_c_part2", "microsoft_xbox_d_part1",
            "microsoft_xbox_d_part2", "microsoft_xbox_e", "microsoft_xbox_f", "microsoft_xbox_g",
            "microsoft_xbox_h", "microsoft_xbox_i", "microsoft_xbox_j", "microsoft_xbox_k",
            "microsoft_xbox_l", "microsoft_xbox_m_part1", "microsoft_xbox_m_part2",
            "microsoft_xbox_n_part1", "microsoft_xbox_n_part2", "microsoft_xbox_o_part1",
            "microsoft_xbox_o_part2", "microsoft_xbox_p", "microsoft_xbox_q", "microsoft_xbox_r",
            "microsoft_xbox_s_part1", "microsoft_xbox_s_part2", "microsoft_xbox_t_part1",
            "microsoft_xbox_t_part2", "microsoft_xbox_u", "microsoft_xbox_v", "microsoft_xbox_w",
            "microsoft_xbox_x", "microsoft_xbox_y", "microsoft_xbox_z",
        ]),
        ("Xbox 360 Archive", "xbox360", [
            "microsoft_xbox360_numberssymbols",
            "microsoft_xbox360_a_part1", "microsoft_xbox360_a_part2", "microsoft_xbox360_b_part1",
            "microsoft_xbox360_b_part2", "microsoft_xbox360_c_part1", "microsoft_xbox360_c_part2",
            "microsoft_xbox360_d_part1", "microsoft_xbox360_d_part2", "microsoft_xbox360_d_part3",
            "microsoft_xbox360_e", "microsoft_xbox360_f_part1", "microsoft_xbox360_f_part2",
            "microsoft_xbox360_g", "microsoft_xbox360_h", "microsoft_xbox360_i", "microsoft_xbox360_j",
            "microsoft_xbox360_k", "microsoft_xbox360_l", "microsoft_xbox360_m_part1",
            "microsoft_xbox360_m_part2", "microsoft_xbox360_n_part1", "microsoft_xbox360_n_part2",
            "microsoft_xbox360_o", "microsoft_xbox360_p", "microsoft_xbox360_q", "microsoft_xbox360_r",
            "microsoft_xbox360_r_part1", "microsoft_xbox360_s_part1", "microsoft_xbox360_s_part2",
            "microsoft_xbox360_t_part1", "microsoft_xbox360_t_part2", "microsoft_xbox360_u",
            "microsoft_xbox360_v", "microsoft_xbox360_w", "microsoft_xbox360_x_part1",
            "microsoft_xbox360_x_part2", "microsoft_xbox360_y", "microsoft_xbox360_z",
        ]),
        ("3DS Encrypted Archive", "n3ds", ["3ds-main-encrypted", "3ds-main-encrypted-p2"]),
        ("NDS Archive", "nds", ["pack-roms-nintendo-ds-eu-usa-jap-rabbits-games"]),
    ];

    /// <summary>Full archive.org /download/ URLs for a set's item identifiers.</summary>
    public static string[] Links(string[] items) =>
        items.Select(i => "https://archive.org/download/" + i).ToArray();
}
