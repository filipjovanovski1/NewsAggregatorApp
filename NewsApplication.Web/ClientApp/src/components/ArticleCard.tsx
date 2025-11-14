import type { ArticleDto } from '../types';
import placeholderImg from '../assets/placeholders/news_placeholder.webp';
export default function ArticleCard({ article }: { article: ArticleDto }) {
    const date = article.publishedUtc ? new Date(article.publishedUtc) : null;

    const imgUrl =
        (article.imageUrl && article.imageUrl.trim().length > 0)
            ? article.imageUrl
            : placeholderImg; // ← imported URL

    return (
        <a className="article-card" href={article.url} target="_blank" rel="noreferrer">
            <div
                className="img"
                style={{ backgroundImage: `url("${imgUrl}")` }}
                aria-hidden="true"
            />
            <div className="meta">
                <h4 title={article.title}>{article.title}</h4>
                <div className="footer">
                    <span className="src">{article.sourceName}</span>
                    {date && <span className="time">{date.toLocaleString()}</span>}
                </div>
            </div>
        </a>
    );
}
