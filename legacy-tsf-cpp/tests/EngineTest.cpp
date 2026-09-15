// EngineTest.cpp —— 输入引擎的单元测试（不需要 TSF，普通控制台程序）
//
// 这一层能独立测试，正是「引擎与 TSF 解耦」的直接好处：
// 键盘流程、候选生成、翻页、上屏这些逻辑占了输入法 80% 的 bug，
// 而它们都可以在这里用几毫秒跑完，不用装输入法、不用重启、不用手打。
//
// 构建：cmake --build build --target EngineTest   （或见 README.md）
// 运行：EngineTest
#include "Dictionary.h"
#include "Engine.h"

#include <cstdio>
#include <string>

namespace {

int g_failed = 0;
int g_passed = 0;

void Print(const std::wstring& text) {
    // 统一按 UTF-8 输出，避免控制台代码页把中文变成乱码
    std::fputs(myime::WideToUtf8(text).c_str(), stdout);
}

// 组装一个测试用键盘输入
myime::KeyInput Key(unsigned vk) {
    myime::KeyInput key;
    key.vk = vk;
    return key;
}

myime::KeyInput Letter(wchar_t c) {
    myime::KeyInput key;
    key.vk = static_cast<unsigned>(c >= L'a' && c <= L'z' ? (c - L'a' + L'A') : c);
    return key;
}

// 依次敲入一串字母（模拟用户输入拼音）
void Type(myime::Engine& engine, const std::wstring& letters) {
    for (wchar_t c : letters) {
        engine.Step(Letter(c));
    }
}

void Check(bool condition, const std::wstring& what) {
    if (condition) {
        ++g_passed;
        Print(L"  [通过] ");
        Print(what);
        Print(L"\n");
    } else {
        ++g_failed;
        Print(L"  [失败] ");
        Print(what);
        Print(L"\n");
    }
}

std::wstring CandidatesText(const myime::Engine& engine) {
    std::wstring text;
    const auto& candidates = engine.Candidates();
    for (size_t i = 0; i < candidates.size(); ++i) {
        if (i > 0) text += L" ";
        text += std::to_wstring(i + 1) + L"." + candidates[i].text;
    }
    return text;
}

bool HasCandidate(const myime::Engine& engine, const std::wstring& text) {
    for (const auto& candidate : engine.Candidates()) {
        if (candidate.text == text) return true;
    }
    return false;
}

}  // namespace

int wmain() {
    ::SetConsoleOutputCP(CP_UTF8);

    myime::Dictionary dictionary;
    dictionary.AddSyllableList(L"a ai an ao ba bai ban bao bei ben bi bian biao bie bin bing bo bu "
                              L"ca cai can cao ce cen ceng cha chan chang chao che chen cheng chi "
                              L"chong chou chu chuan chuang chui chun chuo ci cong cou cu cuan cui "
                              L"cun cuo da dai dan dang dao de deng di dian diao die ding diu dong "
                              L"dou du duan dui dun duo e ei en eng er fa fan fang fei fen feng fo "
                              L"fou fu ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan "
                              L"guang gui gun guo ha hai han hang hao he hei hen heng hong hou hu hua "
                              L"huai huan huang hui hun huo ji jia jian jiang jiao jie jin jing jiong "
                              L"jiu ju juan jue jun ka kai kan kang kao ke ken keng kong kou ku kua "
                              L"kuai kuan kuang kui kun kuo la lai lan lang lao le lei leng li lia lian "
                              L"liang liao lie lin ling liu long lou lu luan lue lun luo ma mai man "
                              L"mang mao me mei men meng mi mian miao mie min ming miu mo mou mu na "
                              L"nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu "
                              L"nong nou nu nuan nuo o ou pa pai pan pang pao pei pen peng pi pian piao "
                              L"pie pin ping po pou pu qi qia qian qiang qiao qie qin qing qiong qiu "
                              L"qu quan que qun ran rang rao re ren reng ri rong rou ru ruan rui run "
                              L"ruo sa sai san sang sao se sen seng sha shai shan shang shao she shei "
                              L"shen sheng shi shou shu shua shuai shuan shuang shui shun shuo si song "
                              L"sou su suan sui sun suo ta tai tan tang tao te teng ti tian tiao tie "
                              L"ting tong tou tu tuan tui tun tuo wa wai wan wang wei wen weng wo wu "
                              L"xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun ya yan "
                              L"yang yao ye yi yin ying yo yong you yu yuan yue yun za zai zan zang "
                              L"zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng zhi "
                              L"zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu "
                              L"zuan zui zun zuo");

    dictionary.LoadFromString(
        "# 测试词典\n"
        "ni 你 5000\n"
        "ni 尼 800\n"
        "ni 泥 600\n"
        "hao 好 5000\n"
        "hao 号 2000\n"
        "hao 豪 500\n"
        "ni hao 你好 9000\n"
        "ni men 你们 6000\n"
        "men 们 4000\n"
        "shi 是 9000\n"
        "shi 时 4000\n"
        "shi 事 3000\n"
        "shi 十 2000\n"
        "shi 使 1500\n"
        "shi 市 1500\n"
        "shi 试 1200\n"
        "shu 输 1500\n"
        "shu 数 2000\n"
        "ru 入 1500\n"
        "fa 法 2000\n"
        "shu ru 输入 6000\n"
        "shu ru fa 输入法 12000\n"
        "# 自定义码表：验证引擎不是写死拼音的\n"
        "ab 甲 1000\n"
        "ac 乙 900\n"
        "ab ac 甲乙 2000\n");

    myime::Engine engine;
    engine.SetDictionary(&dictionary);
    engine.SetPageSize(3);

    Print(L"=== 1. 基本拼音与整词优先 ===\n");
    Type(engine, L"nihao");
    Print(L"  输入 nihao → 候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    Check(engine.Raw() == L"nihao", L"原始输入为 nihao");
    Check(!engine.Candidates().empty() && engine.Candidates()[0].text == L"你好",
          L"首候选是整词「你好」（整词命中优先于单字组合）");
    Check(engine.SegmentationText() == L"ni hao", L"切分结果是 ni hao");

    Print(L"\n=== 2. 空格上屏 ===\n");
    const myime::StepResult commit = engine.Step(Key(VK_SPACE));
    Check(commit.eaten, L"空格被消费");
    Check(commit.commitText == L"你好", L"空格上屏「你好」");
    Check(!engine.IsActive(), L"上屏后组合结束");

    Print(L"\n=== 3. 数字键选字 ===\n");
    Type(engine, L"ni");
    Print(L"  输入 ni → 候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    const myime::StepResult pick = engine.Step(Key('2'));
    Check(pick.eaten && !pick.commitText.empty(), L"数字键 2 选中并上屏");
    Print(L"  上屏内容: ");
    Print(pick.commitText);
    Print(L"\n");
    Check(pick.commitText == L"尼", L"第 2 个候选是「尼」");

    Print(L"\n=== 4. 翻页 ===\n");
    Type(engine, L"shi");   // 7 个候选，每页 3 个 → 3 页
    Check(engine.PageCount() == 3, L"7 个候选 / 每页 3 个 = 3 页");
    Check(engine.PageIndex() == 0 && engine.SelectedIndex() == 0, L"起始在第 1 页第 1 个");
    engine.Step(Key(VK_NEXT));
    Check(engine.PageIndex() == 1, L"PageDown 翻到第 2 页");
    engine.Step(Key(VK_NEXT));
    engine.Step(Key(VK_NEXT));
    Check(engine.PageIndex() == 2, L"再翻页到最后一页后不会越界");
    engine.Step(Key(VK_PRIOR));
    Check(engine.PageIndex() == 1, L"PageUp 翻回第 2 页");
    const myime::StepResult pageCommit = engine.Step(Key('2'));
    Print(L"  第 2 页第 2 个上屏: ");
    Print(pageCommit.commitText);
    Print(L"\n");
    Check(!pageCommit.commitText.empty(), L"翻页后数字键选中当前页的候选");

    Print(L"\n=== 5. 未打完的音节也要有候选（尾巴处理） ===\n");
    Type(engine, L"nih");
    Print(L"  输入 nih → 切分: ");
    Print(engine.SegmentationText());
    Print(L"  候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    Check(HasCandidate(engine, L"你好"), L"nih 的候选里已经出现「你好」");
    engine.Step(Key(VK_ESCAPE));
    Check(!engine.IsActive(), L"Esc 取消组合");

    Print(L"\n=== 6. 退格 ===\n");
    Type(engine, L"niha");
    engine.Step(Key(VK_BACK));
    Check(engine.Raw() == L"nih", L"退格删掉一个字母");
    engine.Step(Key(VK_BACK));
    engine.Step(Key(VK_BACK));
    engine.Step(Key(VK_BACK));
    Check(!engine.IsActive(), L"删空后组合结束");

    Print(L"\n=== 7. 回车 = 英文直通 ===\n");
    Type(engine, L"hello");
    const myime::StepResult enter = engine.Step(Key(VK_RETURN));
    Check(enter.commitText == L"hello", L"回车把原始字母原样上屏");

    Print(L"\n=== 8. 不抢应用的按键 ===\n");
    Type(engine, L"ni");
    myime::KeyInput ctrlA = Letter(L'a');
    ctrlA.ctrl = true;
    Check(!engine.Step(ctrlA).eaten, L"Ctrl+A 不被消费");
    Check(!engine.Step(Key(VK_TAB)).eaten, L"Tab 不被消费");
    engine.Reset();

    Print(L"\n=== 9. 分隔符（xi'an 这类输入） ===\n");
    Type(engine, L"ni");
    engine.Step(Key(VK_OEM_7));
    Type(engine, L"men");
    Print(L"  输入 ni'men → 切分: ");
    Print(engine.SegmentationText());
    Print(L"  候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    Check(HasCandidate(engine, L"你们"), L"ni'men 能切出「你们」");
    engine.Reset();

    Print(L"\n=== 10. 自定义码表（非拼音） ===\n");
    Type(engine, L"abac");
    Print(L"  输入 abac → 候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    Check(HasCandidate(engine, L"甲乙"), L"码表式的双键码也能切成 ab+ac 并组合出「甲乙」");
    engine.Reset();

    Print(L"\n=== 11. 乱码兜底 ===\n");
    Type(engine, L"zzz");
    Print(L"  输入 zzz → 候选: ");
    Print(CandidatesText(engine));
    Print(L"\n");
    Check(HasCandidate(engine, L"zzz"), L"无法识别时仍能把原样字母当候选（不吞输入）");
    const myime::StepResult fallback = engine.Step(Key(VK_SPACE));
    Check(fallback.commitText == L"zzz", L"空格上屏原样字母");
    engine.Reset();

    Print(L"\n----------------------------------------\n");
    Print(L"通过 " + std::to_wstring(g_passed) + L" 项，失败 " + std::to_wstring(g_failed) + L" 项\n");
    return g_failed == 0 ? 0 : 1;
}
