﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ProteoformSuiteInternal;
using System.IO;

namespace ProteoformSuite
{
    public partial class TopDown : Form
    {
        private static Color[] colors = new Color[20];
        private static List<string> mods = new List<string>();

        public TopDown()
        {
            InitializeComponent();
        }

        public void load_dgv()
        {
            DisplayUtility.FillDataGridView(dgv_TD_proteoforms, Lollipop.proteoform_community.topdown_proteoform_groups.Select(g => g.root));
            load_colors();
            load_ptm_colors();
        }

        public void load_topdown()
        {
          if (Lollipop.top_down_hits.Count == 0 && Lollipop.input_files.Any(f => f.purpose == Purpose.TopDown))
            {
                 Lollipop.process_td_results();
            }
        }

        private void bt_load_td_Click(object sender, EventArgs e)
        {
            Lollipop.proteoform_community.topdown_proteoform_groups.Clear();
            clear_lists();
            Lollipop.aggregate_td_hits();
                tb_tdProteoforms.Text = Lollipop.proteoform_community.topdown_proteoform_groups.Count.ToString();
            load_dgv();
        }

        private void clear_lists()
        {
            Lollipop.td_relations.Clear();
            foreach (Proteoform p in Lollipop.proteoform_community.experimental_proteoforms) p.relationships.RemoveAll(r => r.relation_type == ProteoformComparison.etd);
            foreach (Proteoform p in Lollipop.proteoform_community.theoretical_proteoforms) p.relationships.RemoveAll(r => r.relation_type == ProteoformComparison.ttd);
            dgv_TD_proteoforms.DataSource = null;
            dgv_TD_proteoforms.Rows.Clear();
        }

        private void bt_td_relations_Click(object sender, EventArgs e)
        {
            if (Lollipop.proteoform_community.experimental_proteoforms.Length > 0 && Lollipop.proteoform_community.topdown_proteoform_groups.Count > 0)
            {
                clear_lists();
                Lollipop.make_td_relationships();
                tb_td_relations.Text = Lollipop.td_relations.Count.ToString();
                load_dgv();
            }
            else
            {
                if (Lollipop.proteoform_community.experimental_proteoforms.Length > 0) MessageBox.Show("Go back and load in topdown results.");
                else if (Lollipop.proteoform_community.topdown_proteoform_groups.Count > 0) MessageBox.Show("Go back and aggregate experimental proteoforms.");
            }
        }

        private void dgv_TD_proteoforms_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                dgv_TD_family.DataSource = null;
                TopDownProteoform p = (TopDownProteoform)this.dgv_TD_proteoforms.Rows[e.RowIndex].DataBoundItem;
                if (p.relationships != null)
                {
                    DisplayUtility.FillDataGridView(dgv_TD_family, p.topdown_group.relationships);  //show T-TD and E-TD relationsj
                }
                get_proteoform_sequence(p);
            }
        }

        private void get_proteoform_sequence(TopDownProteoform p)
        {
            rtb_sequence.Text = p.sequence + "\n";
            rtb_sequence.SelectionStart = 0;
            rtb_sequence.SelectionLength = p.sequence.Length;
            rtb_sequence.SelectionColor = Color.Black;
            rtb_sequence.ZoomFactor = 3;

            int length = p.sequence.Length + 1;

            foreach (Ptm ptm in p.ptm_list)
            {
                int i;
                try { i = mods.IndexOf(ptm.modification.description); }
                catch { i = 0; } //just make color blue if > 20 unique PTMs
                Color color = colors[i];

                rtb_sequence.SelectionStart = ptm.position;
                rtb_sequence.SelectionLength = 1;
                rtb_sequence.SelectionColor = color;
            }

            foreach (string description in p.ptm_list.Select(ptm => ptm.modification.description).Distinct())
            {
                int i;
                try { i = mods.IndexOf(description); }
                catch { i = 0; }
                Color color = colors[i];

                rtb_sequence.AppendText("\n" + description);
                rtb_sequence.SelectionStart = length;
                rtb_sequence.SelectionLength = description.Length + 1;
                rtb_sequence.SelectionColor = colors[i];
                length += description.Length + 1;
            }

        }

        private static void load_colors()
        {
            colors[0] = Color.Blue;
            colors[1] = Color.Red;
            colors[2] = Color.Orange;
            colors[3] = Color.Green;
            colors[4] = Color.Purple;
            colors[5] = Color.Gold;
            colors[6] = Color.DeepPink;
            colors[7] = Color.ForestGreen;
            colors[8] = Color.DarkBlue;
            colors[9] = Color.DarkOrange;
            colors[10] = Color.OrangeRed;
            colors[11] = Color.Magenta;
            colors[12] = Color.DeepSkyBlue;
            colors[13] = Color.DarkSlateGray;
            colors[14] = Color.DarkSalmon;
            colors[15] = Color.DarkTurquoise;
            colors[16] = Color.Aqua;
            colors[17] = Color.DarkOliveGreen;
            colors[18] = Color.Fuchsia;
            colors[19] = Color.HotPink;
        }

        private static void load_ptm_colors()
        {
            List<Ptm> ptm = new List<Ptm>();
            foreach (TopDownProteoform p in Lollipop.proteoform_community.topdown_proteoform_groups.Select(t => t.root))
            {
                ptm.AddRange(p.ptm_list);
            }
            IEnumerable<string> unique_ptm = ptm.Select(p => p.modification.description).Distinct();
            mods = unique_ptm.ToList();
        }

        private void TopDown_Load(object sender, EventArgs e)
        {

        }
    }
}
