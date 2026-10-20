using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MintPet
{
    /// <summary>Small modal dialog for entering the DeepSeek API key.</summary>
    internal sealed class ApiKeyDialog : Form
    {
        private readonly TextBox keyBox;

        public string ApiKey
        {
            get { return keyBox.Text.Trim(); }
        }

        public ApiKeyDialog(string currentKey)
        {
            Text = "设置 DeepSeek API Key";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 236);
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;

            Label title = new Label();
            title.Text = "DeepSeek API Key";
            title.Font = new Font(Font, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 16);
            Controls.Add(title);

            keyBox = new TextBox();
            keyBox.Location = new Point(20, 44);
            keyBox.Size = new Size(430, 26);
            keyBox.UseSystemPasswordChar = true;
            keyBox.Text = currentKey ?? "";
            Controls.Add(keyBox);

            CheckBox reveal = new CheckBox();
            reveal.Text = "显示明文";
            reveal.AutoSize = true;
            reveal.Location = new Point(20, 78);
            reveal.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !reveal.Checked; };
            Controls.Add(reveal);

            LinkLabel link = new LinkLabel();
            link.Text = "在 platform.deepseek.com/api_keys 获取 Key";
            link.AutoSize = true;
            link.Location = new Point(20, 106);
            link.LinkClicked += delegate
            {
                try { Process.Start("https://platform.deepseek.com/api_keys"); }
                catch { }
            };
            Controls.Add(link);

            Label note = new Label();
            note.Text = "Key 只保存在本机（Windows DPAPI 加密），仅用于查询余额，不会上传到别处。";
            note.ForeColor = Color.FromArgb(90, 90, 100);
            note.AutoSize = true;
            note.MaximumSize = new Size(428, 0);
            note.Location = new Point(20, 134);
            Controls.Add(note);

            Button save = new Button();
            save.Text = "保存";
            save.DialogResult = DialogResult.OK;
            save.Location = new Point(272, 192);
            save.Size = new Size(88, 30);
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(368, 192);
            cancel.Size = new Size(88, 30);
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        protected override void Dispose(bool disposing)
        {
            // best effort: do not leave the plain key sitting in the text box
            if (disposing && keyBox != null) keyBox.Text = "";
            base.Dispose(disposing);
        }
    }
}
